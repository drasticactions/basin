using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public sealed class PortalScreenshotModule : PortalModule, IScreenshotHandler, IScreenshotProperties
{
    public const uint TargetScreen = 1;

    public const uint TargetWindow = 2;

    public const uint TargetArea = 4;

    public const uint TargetActiveWindow = 8;

    private IScreenCapture _capture = null!;
    private IPortalPrompts _prompts = null!;
    private OutputLayout _layout = null!;
    private IToplevelModel? _toplevels;
    private IToplevelStack? _stack;
    private PortalOptions _options = new();
    private ToplevelInfo[] _toplevelScratch = new ToplevelInfo[32];
    private ulong[] _stackScratch = new ulong[32];

    public override string WireInterface => "org.freedesktop.impl.portal.Screenshot";

    public override int Version => 3;

    public override IReadOnlyList<Type> Capabilities => [typeof(IToplevelModel), typeof(IToplevelStack), typeof(IAppInfoResolver)];

    public override IReadOnlyList<Type> Drivers => [typeof(IScreenCapture), typeof(IPortalPrompts)];

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalScreenshot;

    public uint AvailableTargets => TargetScreen | TargetArea | (_toplevels is null ? 0 : TargetWindow) | (_toplevels is null || _stack is null ? 0 : TargetActiveWindow);

    uint IScreenshotProperties.Version => (uint)Version;

    protected override void Bind(BasinServices services)
    {
        _capture = services.Require<IScreenCapture>();
        _prompts = services.Require<IPortalPrompts>();
        _layout = services.Require<OutputLayout>();
        _toplevels = services.Find<IToplevelModel>();
        _stack = services.Find<IToplevelStack>();
        _options = services.Find<PortalOptions>() ?? new PortalOptions();
    }

    ValueTask IScreenshotHandler.HandleGetPropertyAsync(IScreenshotHandler.GetPropertyContext context) => context.Handle(this);

    ValueTask IScreenshotHandler.HandleGetAllPropertiesAsync(IScreenshotHandler.GetAllPropertiesContext context) => context.Handle(this);

    public async ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> ScreenshotAsync(
        ObjectPath handle, string appId, string parentWindow, Dictionary<string, VariantValue> options)
    {
        var request = TrackRequest(handle);
        try
        {
            var interactive = Vardict.Bool(options, "interactive");
            var modal = Vardict.Bool(options, "modal", true);
            var checkedByFrontend = Vardict.Bool(options, "permission_store_checked");
            var hasTarget = Vardict.Has(options, "target");
            var target = Vardict.UInt32(options, "target", TargetScreen);
            if ((target & ~AvailableTargets) != 0 || target == 0 || (target & (target - 1)) != 0)
            {
                Log.Info($"screenshot for {appId}: target {target} is not one this compositor offers");
                return (PortalResponse.Other, Results.Empty);
            }

            Box? area = null;
            var toplevelId = 0UL;
            if (!interactive)
            {
                if (!checkedByFrontend)
                {
                    var confirm = await _prompts.Confirm(
                        Named(new ConfirmPrompt(appId, parentWindow, modal, "Take a screenshot?", $"{Describe(appId)} wants to take a screenshot")),
                        request.Token).ConfigureAwait(true);
                    if (!confirm.IsAccepted || !confirm.Value)
                    {
                        return (ResponseFor(confirm.Response, request), Results.Empty);
                    }
                }

                if (target is TargetWindow or TargetActiveWindow)
                {
                    toplevelId = ActiveToplevel();
                    if (toplevelId == 0)
                    {
                        return (PortalResponse.Other, Results.Empty);
                    }
                }
            }
            else if (!hasTarget || target == TargetArea)
            {
                var picked = await _prompts.SelectArea(Named(new AreaPrompt(appId, parentWindow, modal, null, false)), request.Token).ConfigureAwait(true);
                if (!picked.IsAccepted)
                {
                    return (ResponseFor(picked.Response, request), Results.Empty);
                }

                area = picked.Value.IsEmpty ? null : picked.Value;
            }
            else if (target == TargetWindow)
            {
                var prompt = Named(new SourcePrompt(appId, parentWindow, modal, PromptSourceKinds.Window, false, false, [], SnapshotToplevels()));
                var chosen = await _prompts.SelectSources(prompt, request.Token).ConfigureAwait(true);
                if (!chosen.IsAccepted || chosen.Value.Sources.Count == 0)
                {
                    return (ResponseFor(chosen.Response, request), Results.Empty);
                }

                toplevelId = chosen.Value.Sources[0].ToplevelId;
            }
            else
            {
                var confirm = await _prompts.Confirm(
                    Named(new ConfirmPrompt(appId, parentWindow, modal, "Take a screenshot?", $"{Describe(appId)} wants to take a screenshot")),
                    request.Token).ConfigureAwait(true);
                if (!confirm.IsAccepted || !confirm.Value)
                {
                    return (ResponseFor(confirm.Response, request), Results.Empty);
                }

                if (target == TargetActiveWindow)
                {
                    toplevelId = ActiveToplevel();
                    if (toplevelId == 0)
                    {
                        return (PortalResponse.Other, Results.Empty);
                    }
                }
            }

            if (request.IsClosed)
            {
                return (PortalResponse.Other, Results.Empty);
            }

            var source = toplevelId != 0
                ? CaptureSource.Toplevel(toplevelId)
                : CaptureSource.Region(area ?? _layout.Bounds, 0, overlayCursor: true);
            var shot = Capture(source);
            if (shot is null)
            {
                Log.Warn($"screenshot for {appId}: the capture failed");
                return (PortalResponse.Other, Results.Empty);
            }

            string path;
            try
            {
                var directory = _options.ResolveScreenshotDirectory();
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, $"Screenshot from {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");
                for (var n = 1; File.Exists(path); n++)
                {
                    path = Path.Combine(directory, $"Screenshot from {DateTime.Now:yyyy-MM-dd HH-mm-ss} ({n}).png");
                }

                BufferCapture.WritePng(shot, path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"screenshot for {appId}: the file was not written: {e.Message}");
                return (PortalResponse.Other, Results.Empty);
            }
            finally
            {
                shot.Destroy();
            }

            Log.Info($"screenshot for {appId}: {path}");
            return (PortalResponse.Success, Results.With("uri", VariantValue.String(new Uri(path).AbsoluteUri)));
        }
        finally
        {
            ReleaseRequest(request);
        }
    }

    public async ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> PickColorAsync(
        ObjectPath handle, string appId, string parentWindow, Dictionary<string, VariantValue> options)
    {
        var request = TrackRequest(handle);
        try
        {
            var modal = Vardict.Bool(options, "modal", true);
            var picked = await _prompts.SelectArea(Named(new AreaPrompt(appId, parentWindow, modal, null, true)), request.Token).ConfigureAwait(true);
            if (!picked.IsAccepted || request.IsClosed)
            {
                return (ResponseFor(picked.Response, request), Results.Empty);
            }

            var point = picked.Value;
            var pixel = Capture(CaptureSource.Region(new Box(point.X, point.Y, 1, 1), 1));
            if (pixel is null)
            {
                return (PortalResponse.Other, Results.Empty);
            }

            try
            {
                if (!pixel.BeginDataAccess(BufferDataAccess.Read, out var view))
                {
                    return (PortalResponse.Other, Results.Empty);
                }

                uint value;
                unsafe
                {
                    value = *(uint*)view.Data;
                }

                pixel.EndDataAccess();
                var color = VariantValue.Struct(
                    VariantValue.Double(SrgbToLinear((byte)(value >> 16))),
                    VariantValue.Double(SrgbToLinear((byte)(value >> 8))),
                    VariantValue.Double(SrgbToLinear((byte)value)));
                return (PortalResponse.Success, Results.With("color", color));
            }
            finally
            {
                pixel.Destroy();
            }
        }
        finally
        {
            ReleaseRequest(request);
        }
    }

    internal static double SrgbToLinear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    internal static uint ResponseFor(PromptResponse response, PortalRequest request) => response switch
    {
        PromptResponse.Denied => PortalResponse.Cancelled,
        _ when request.IsClosed => PortalResponse.Other,
        PromptResponse.Cancelled => PortalResponse.Other,
        _ => PortalResponse.Success,
    };

    private MemoryBuffer? Capture(in CaptureSource source)
    {
        if (!_capture.TryDescribe(source, out var format) || format.Width <= 0 || format.Height <= 0)
        {
            return null;
        }

        var buffer = new MemoryBuffer(format.Width, format.Height, format.Format);
        if (_capture.Capture(source, default, buffer))
        {
            return buffer;
        }

        buffer.Destroy();
        return null;
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

    private ulong ActiveToplevel()
    {
        if (_toplevels is not { } model)
        {
            return 0;
        }

        if (_stack is { } stack)
        {
            int count;
            while ((count = stack.Enumerate(_stackScratch)) < 0)
            {
                _stackScratch = new ulong[_stackScratch.Length * 2];
            }

            for (var i = count - 1; i >= 0; i--)
            {
                if (model.TryGet(_stackScratch[i], out var info) && (info.State & ToplevelState.ExcludedFromCapture) == 0)
                {
                    return _stackScratch[i];
                }
            }
        }

        int total;
        while ((total = model.Enumerate(_toplevelScratch)) < 0)
        {
            _toplevelScratch = new ToplevelInfo[_toplevelScratch.Length * 2];
        }

        for (var i = 0; i < total; i++)
        {
            if ((_toplevelScratch[i].State & ToplevelState.Activated) != 0)
            {
                return _toplevelScratch[i].Id;
            }
        }

        return 0;
    }
}
