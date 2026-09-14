using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public sealed class PortalScreenCastModule : PortalModule, IScreenCastHandler, IScreenCastProperties
{
    public const uint CursorHidden = 1;

    public const uint CursorEmbedded = 2;

    public const uint CursorMetadata = 4;

    private static ulong _nextStreamId;

    private IScreenCapture _capture = null!;
    private IScreencastPublisher _publisher = null!;
    private IPortalPrompts _prompts = null!;
    private OutputLayout _layout = null!;
    private IOutputSet _outputs = null!;
    private IToplevelModel? _toplevels;
    private IDmabufCapture? _dmabuf;
    private PortalOptions _options = new();
    private ToplevelInfo[] _toplevelScratch = new ToplevelInfo[32];

    public override string WireInterface => "org.freedesktop.impl.portal.ScreenCast";

    public override int Version => 6;

    public override IReadOnlyList<Type> Capabilities => [typeof(IToplevelModel), typeof(IDmabufCapture), typeof(IAppInfoResolver)];

    public override IReadOnlyList<Type> Drivers => [typeof(IScreenCapture), typeof(IScreencastPublisher), typeof(IPortalPrompts), typeof(IOutputSet)];

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalScreenCast;

    public uint AvailableSourceTypes =>
        (uint)ScreenCastSourceKind.Monitor
        | (_toplevels is null ? 0 : (uint)ScreenCastSourceKind.Window);

    public uint AvailableCursorModes => CursorHidden | CursorEmbedded | CursorMetadata;

    uint IScreenCastProperties.Version => (uint)Version;

    public override void SeedDefaults(BasinServices services)
    {
        base.SeedDefaults(services);
        if (services.Find<OutputLayout>() is { } layout)
        {
            services.UseDefault<IOutputSet>(new LayoutOutputSet(layout));
        }
    }

    protected override void Bind(BasinServices services)
    {
        _capture = services.Require<IScreenCapture>();
        _publisher = services.Require<IScreencastPublisher>();
        _prompts = services.Require<IPortalPrompts>();
        _layout = services.Require<OutputLayout>();
        _outputs = services.Require<IOutputSet>();
        _toplevels = services.Find<IToplevelModel>();
        _dmabuf = services.Find<IDmabufCapture>();
        _options = services.Find<PortalOptions>() ?? new PortalOptions();
        _outputs.Changed += OnOutputsChanged;
    }

    protected override void Unbind() => _outputs.Changed -= OnOutputsChanged;

    ValueTask IScreenCastHandler.HandleGetPropertyAsync(IScreenCastHandler.GetPropertyContext context) => context.Handle(this);

    ValueTask IScreenCastHandler.HandleGetAllPropertiesAsync(IScreenCastHandler.GetAllPropertiesContext context) => context.Handle(this);

    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> CreateSessionAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        var bus = Bus ?? throw new InvalidOperationException("the portal bus is not connected");
        var session = new ScreenCastSession(this, bus, sessionHandle, appId);
        bus.Register(session);
        return ValueTask.FromResult((PortalResponse.Success, Results.With("session_id", VariantValue.String(session.Id))));
    }

    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> SelectSourcesAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not IScreenCastCarrier carrier)
        {
            return ValueTask.FromResult((PortalResponse.Other, Results.Empty));
        }

        var state = carrier.ScreenCast;
        var types = Vardict.UInt32(options, "types", (uint)ScreenCastSourceKind.Monitor) & AvailableSourceTypes;
        var cursor = Vardict.UInt32(options, "cursor_mode", CursorHidden);
        if ((cursor & ~AvailableCursorModes) != 0 || cursor == 0 || (cursor & (cursor - 1)) != 0)
        {
            Log.Info($"screencast for {appId}: cursor mode {cursor} is not offered, closing the session");
            carrier.Session.Close();
            return ValueTask.FromResult((PortalResponse.Other, Results.Empty));
        }

        state.Types = types == 0 ? (uint)ScreenCastSourceKind.Monitor : types;
        state.Multiple = Vardict.Bool(options, "multiple");
        state.CursorMode = cursor switch
        {
            CursorEmbedded => ScreencastCursorMode.Embedded,
            CursorMetadata => ScreencastCursorMode.Metadata,
            _ => ScreencastCursorMode.Hidden,
        };
        state.PersistMode = Math.Min(Vardict.UInt32(options, "persist_mode"), 2);
        state.RestoreCandidates = null;
        if (Vardict.RestoreData(options, "restore_data") is { } restore)
        {
            state.RestoreCandidates = RestoreData.Decode(restore.Vendor, _options.RestoreVendor, restore.Version, restore.Data);
        }

        state.SourcesSelected = true;
        return ValueTask.FromResult((PortalResponse.Success, Results.Empty));
    }

    public async ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> StartAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, string parentWindow, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not IScreenCastCarrier carrier)
        {
            return (PortalResponse.Other, Results.Empty);
        }

        var request = TrackRequest(handle);
        try
        {
            var (response, results) = await StartStreamsAsync(carrier, request, appId, parentWindow, Vardict.Bool(options, "modal", true)).ConfigureAwait(true);
            return (response, results);
        }
        finally
        {
            ReleaseRequest(request);
        }
    }

    public async ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> StartStreamsAsync(
        IScreenCastCarrier carrier, PortalRequest request, string appId, string parentWindow, bool modal)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        ArgumentNullException.ThrowIfNull(request);
        var state = carrier.ScreenCast;
        var session = carrier.Session;
        if (state.Started || session.IsClosed)
        {
            return (PortalResponse.Other, Results.Empty);
        }

        IReadOnlyList<ScreenCastSource>? chosen = null;
        var persist = 0u;
        if (state.RestoreCandidates is { } candidates && TryResolve(candidates, state.Types, out var restored))
        {
            chosen = restored;
            persist = state.PersistMode;
            Log.Info($"screencast for {appId}: restored {chosen.Count} source(s) without a prompt");
        }
        else
        {
            var kinds = (PromptSourceKinds)state.Types;
            var prompt = Named(new SourcePrompt(appId, parentWindow, modal, kinds, state.Multiple, state.PersistMode != 0, SnapshotOutputs(), SnapshotToplevels()));
            var answer = await _prompts.SelectSources(prompt, request.Token).ConfigureAwait(true);
            if (!answer.IsAccepted || answer.Value.Sources.Count == 0 || session.IsClosed)
            {
                return (PortalScreenshotModule.ResponseFor(answer.Response, request), Results.Empty);
            }

            chosen = Translate(answer.Value.Sources, state.Multiple);
            persist = Math.Min(answer.Value.PersistMode, state.PersistMode);
        }

        foreach (var source in chosen)
        {
            if (!TryPublish(state, source, appId, out var reason))
            {
                Log.Warn($"screencast for {appId}: {reason}");
                ReleaseStreams(state);
                return (PortalResponse.Other, Results.Empty);
            }
        }

        state.Started = true;
        var results = new Dictionary<string, VariantValue>
        {
            ["streams"] = StreamsResult(state.Streams),
        };
        if (persist != 0)
        {
            results["persist_mode"] = VariantValue.UInt32(persist);
            results["restore_data"] = RestoreData.Encode(_options.RestoreVendor, state.Streams);
        }

        return (PortalResponse.Success, results);
    }

    public static VariantValue StreamsResult(IReadOnlyList<ScreenCastStream> streams)
    {
        ArgumentNullException.ThrowIfNull(streams);
        var list = new List<(uint, Dictionary<string, VariantValue>)>(streams.Count);
        foreach (var stream in streams)
        {
            var properties = new Dictionary<string, VariantValue>
            {
                ["size"] = Results.IntPair(stream.LayoutBox.Width, stream.LayoutBox.Height),
                ["source_type"] = VariantValue.UInt32((uint)stream.Source.Kind),
                ["mapping_id"] = VariantValue.String(stream.MappingId),
            };
            if (stream.Source.Kind != ScreenCastSourceKind.Window)
            {
                properties["position"] = Results.IntPair(stream.LayoutBox.X, stream.LayoutBox.Y);
            }

            if (stream.Serial != 0)
            {
                properties["pipewire-serial"] = VariantValue.UInt64(stream.Serial);
            }

            list.Add((stream.NodeId, properties));
        }

        return Results.Streams(list);
    }

    internal void ReleaseStreams(ScreenCastState state)
    {
        foreach (var stream in state.StreamList)
        {
            _publisher.Close(stream.Id);
        }

        state.StreamList.Clear();
        state.Started = false;
    }

    private bool TryPublish(ScreenCastState state, ScreenCastSource source, string appId, out string reason)
    {
        CaptureSource capture;
        Box box;
        string mappingId;
        var embed = state.CursorMode == ScreencastCursorMode.Embedded;
        switch (source.Kind)
        {
            case ScreenCastSourceKind.Monitor when source.Output is { } output:
                capture = CaptureSource.Output(output, overlayCursor: embed);
                box = _layout.BoxOf(output);
                mappingId = output.Name;
                break;

            case ScreenCastSourceKind.Window:
                if (_toplevels is null || !_toplevels.TryGet(source.ToplevelId, out var info))
                {
                    reason = $"toplevel {source.ToplevelId} is gone";
                    return false;
                }

                capture = CaptureSource.Toplevel(source.ToplevelId, overlayCursor: embed);
                box = new Box(0, 0, info.Geometry.Width, info.Geometry.Height);
                mappingId = $"toplevel-{source.ToplevelId}";
                break;

            default:
                reason = "the source names no output";
                return false;
        }

        var streamId = Interlocked.Increment(ref _nextStreamId);
        var request = new ScreencastRequest
        {
            StreamId = streamId,
            Source = capture,
            Cursor = state.CursorMode,
            Dmabuf = _dmabuf,
        };
        if (!_publisher.TryPublish(request, out var info2))
        {
            reason = info2.FailureReason ?? "the publisher refused the stream";
            return false;
        }

        var stream = new ScreenCastStream(streamId, info2.NodeId, info2.ObjectSerial, source, source.Output, box, mappingId, info2.DmabufOffered);
        state.StreamList.Add(stream);
        Log.Info($"screencast for {appId}: stream {streamId} on node {info2.NodeId} ({mappingId})");
        reason = "";
        return true;
    }

    private IReadOnlyList<ScreenCastSource> Translate(IReadOnlyList<SelectedSource> selected, bool multiple)
    {
        var list = new List<ScreenCastSource>();
        foreach (var source in selected)
        {
            switch (source.Kind)
            {
                case PromptSourceKinds.Monitor when source.Output is { } output:
                    list.Add(new ScreenCastSource(ScreenCastSourceKind.Monitor, output, 0, output.Name));
                    break;
                case PromptSourceKinds.Window when _toplevels is { } model && model.TryGet(source.ToplevelId, out var info):
                    list.Add(new ScreenCastSource(ScreenCastSourceKind.Window, null, source.ToplevelId, AppId: info.AppId, Title: info.Title));
                    break;
            }

            if (!multiple && list.Count == 1)
            {
                break;
            }
        }

        return list;
    }

    private bool TryResolve(IReadOnlyList<ScreenCastSource> candidates, uint types, out IReadOnlyList<ScreenCastSource> resolved)
    {
        var list = new List<ScreenCastSource>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if ((types & (uint)candidate.Kind) == 0)
            {
                resolved = [];
                return false;
            }

            switch (candidate.Kind)
            {
                case ScreenCastSourceKind.Monitor:
                    IOutput? found = null;
                    foreach (var (output, _) in _layout.Outputs)
                    {
                        if (output.Name == candidate.OutputName)
                        {
                            found = output;
                            break;
                        }
                    }

                    if (found is null)
                    {
                        resolved = [];
                        return false;
                    }

                    list.Add(candidate with { Output = found });
                    break;

                case ScreenCastSourceKind.Window:
                    var id = FindToplevel(candidate.AppId, candidate.Title);
                    if (id == 0)
                    {
                        resolved = [];
                        return false;
                    }

                    list.Add(candidate with { ToplevelId = id });
                    break;

                default:
                    resolved = [];
                    return false;
            }
        }

        resolved = list;
        return list.Count > 0;
    }

    private ulong FindToplevel(string appId, string title)
    {
        if (_toplevels is not { } model)
        {
            return 0;
        }

        int count;
        while ((count = model.Enumerate(_toplevelScratch)) < 0)
        {
            _toplevelScratch = new ToplevelInfo[_toplevelScratch.Length * 2];
        }

        for (var i = 0; i < count; i++)
        {
            var info = _toplevelScratch[i];
            if (info.AppId == appId && info.Title == title && (info.State & ToplevelState.ExcludedFromCapture) == 0)
            {
                return info.Id;
            }
        }

        return 0;
    }

    private IReadOnlyList<PromptOutput> SnapshotOutputs()
    {
        var list = new List<PromptOutput>();
        foreach (var (output, _) in _layout.Outputs)
        {
            list.Add(new PromptOutput(output, output.Name, output.Description, _layout.BoxOf(output), output.Scale));
        }

        return list;
    }

    private IReadOnlyList<PromptToplevel> SnapshotToplevels()
    {
        if (_toplevels is not { } model)
        {
            return [];
        }

        int count;
        while ((count = model.Enumerate(_toplevelScratch)) < 0)
        {
            _toplevelScratch = new ToplevelInfo[_toplevelScratch.Length * 2];
        }

        var list = new List<PromptToplevel>(count);
        for (var i = 0; i < count; i++)
        {
            var info = _toplevelScratch[i];
            if ((info.State & ToplevelState.ExcludedFromCapture) == 0)
            {
                list.Add(new PromptToplevel(info.Id, info.Title, info.AppId));
            }
        }

        return list;
    }

    private void OnOutputsChanged()
    {
        if (Bus is not { } bus)
        {
            return;
        }

        foreach (var session in bus.Sessions.Values.ToArray())
        {
            if (session is not IScreenCastCarrier carrier)
            {
                continue;
            }

            foreach (var stream in carrier.ScreenCast.Streams)
            {
                if (stream.Output is { } output && !_outputs.Outputs.Contains(output))
                {
                    Log.Info($"screencast session {session.Id}: output {output.Name} is gone, closing");
                    session.Close();
                    break;
                }
            }
        }
    }
}
