using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

/// <summary>
/// Serves <c>org.freedesktop.impl.portal.Screenshot</c>. It writes screenshots as PNG files and picks colors through <see cref="IScreenCapture"/>.
/// </summary>
public sealed class PortalScreenshotModule : PortalModule, IScreenshotHandler, IScreenshotProperties
{
    /// <summary>
    /// The screenshot target bit for the whole layout.
    /// </summary>
    public const uint TargetScreen = 1;

    /// <summary>
    /// The screenshot target bit for a window that the user picks.
    /// </summary>
    public const uint TargetWindow = 2;

    /// <summary>
    /// The screenshot target bit for an area that the user selects.
    /// </summary>
    public const uint TargetArea = 4;

    /// <summary>
    /// The screenshot target bit for the window on top of the stack.
    /// </summary>
    public const uint TargetActiveWindow = 8;

    private IScreenCapture _capture = null!;
    private IPortalPrompts _prompts = null!;
    private OutputLayout _layout = null!;
    private IToplevelModel? _toplevels;
    private IToplevelStack? _stack;
    private PortalOptions _options = new();
    private ToplevelInfo[] _toplevelScratch = new ToplevelInfo[32];
    private ulong[] _stackScratch = new ulong[32];

    /// <inheritdoc/>
    public override string WireInterface => "org.freedesktop.impl.portal.Screenshot";

    /// <inheritdoc/>
    public override int Version => 3;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IToplevelModel), typeof(IToplevelStack), typeof(IAppInfoResolver)];

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Drivers => [typeof(IScreenCapture), typeof(IPortalPrompts)];

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalScreenshot;

    /// <summary>
    /// Gets the target bits that the module offers. The window targets need an <see cref="IToplevelModel"/>, and the active window also needs an <see cref="IToplevelStack"/>.
    /// </summary>
    public uint AvailableTargets => TargetScreen | TargetArea | (_toplevels is null ? 0 : TargetWindow) | (_toplevels is null || _stack is null ? 0 : TargetActiveWindow);

    uint IScreenshotProperties.Version => (uint)Version;

    /// <inheritdoc/>
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

    /// <summary>
    /// Captures the requested target and writes it as a PNG file in <see cref="PortalOptions.ResolveScreenshotDirectory"/>. A non-interactive call asks for confirmation unless the frontend already checked the permission.
    /// </summary>
    /// <param name="handle">The request object path.</param>
    /// <param name="appId">The application that asks.</param>
    /// <param name="parentWindow">The parent window identifier of the application.</param>
    /// <param name="options">The <c>interactive</c>, <c>modal</c>, <c>permission_store_checked</c> and <c>target</c> options.</param>
    /// <returns>The <c>uri</c> of the file, or a cancel or other response when the target, the prompt, the capture or the write fails.</returns>
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
                        return (PortalRefusal.RefusalFor(confirm), Results.Empty);
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
                    return (PortalRefusal.RefusalFor(picked.Response), Results.Empty);
                }

                area = picked.Value.IsEmpty ? null : picked.Value;
            }
            else if (target == TargetWindow)
            {
                var prompt = Named(new SourcePrompt(appId, parentWindow, modal, PromptSourceKinds.Window, false, false, [], SnapshotToplevels()));
                var chosen = await _prompts.SelectSources(prompt, request.Token).ConfigureAwait(true);
                if (!chosen.IsAccepted || chosen.Value.Sources.Count == 0)
                {
                    return (PortalRefusal.RefusalFor(chosen.Response), Results.Empty);
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
                    return (PortalRefusal.RefusalFor(confirm), Results.Empty);
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

    /// <summary>
    /// Asks the user to pick a point and reads the color of that pixel.
    /// </summary>
    /// <param name="handle">The request object path.</param>
    /// <param name="appId">The application that asks.</param>
    /// <param name="parentWindow">The parent window identifier of the application.</param>
    /// <param name="options">The <c>modal</c> option.</param>
    /// <returns>The <c>color</c> result as sRGB red, green and blue from 0 to 1, or a cancel or other response.</returns>
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
                return (PortalRefusal.RefusalFor(picked.Response), Results.Empty);
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
                    VariantValue.Double(((byte)(value >> 16)) / 255.0),
                    VariantValue.Double(((byte)(value >> 8)) / 255.0),
                    VariantValue.Double(((byte)value) / 255.0));
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
