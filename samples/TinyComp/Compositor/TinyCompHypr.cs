using Basin;
using Basin.Diagnostics;
using Basin.Hypr;
using Basin.Hypr.InputCapture;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private HyprShortcuts _hyprShortcuts = null!;
    private HyprCtm? _hyprCtm;
    private HyprlandGlobalShortcutsManager? _hyprShortcutManager;
    private HyprlandInputCaptureManager? _inputCapture;

    internal void WarnNoCtmShader() =>
        _log.Warn($"{_rendererName} compiles no pixel shader dialect; a CTM on this backend is ignored");

    internal Basin.Host.OutputView? ViewOf(IOutput output)
    {
        foreach (var view in Views)
        {
            if (ReferenceEquals(view.Output, output))
            {
                return view;
            }
        }

        return null;
    }

    internal IPixelShader? CompilePostShader(in PixelShaderSource source, PixelShaderUniform[] uniforms) =>
        _renderer.CompilePixelShader(source, uniforms);

    private ProtocolPack? HyprPackFor(Config config)
    {
        if (!config.HyprEnabled)
        {
            return null;
        }

        var pack = HyprPack.Default;
        if (!config.HyprCtm)
        {
            pack = pack.Without("hyprland_ctm_control_manager_v1");
        }

        if (config.HyprInputCapture)
        {
            pack += InputCapturePack.Default;
        }

        return pack;
    }

    private void WireInputCapture()
    {
        _inputCapture = _services.Find<HyprlandInputCaptureManager>();
        if (_inputCapture is { } capture)
        {
            capture.WarpRequested += (x, y) => MoveCursor(x, y, (uint)Environment.TickCount);
        }
    }

    private bool CaptureMotion(uint time, double x, double y, double dx, double dy) =>
        _inputCapture is { } capture && capture.NotifyMotion(time, x, y, dx, dy);

    private bool HandleHyprShortcut(uint key, bool pressed) =>
        !_sessionLock.IsLocked
        && _hyprShortcutManager is { } manager
        && _hyprShortcuts.HandleKey(key, _seat.Keyboard.RawKeysymFor(key).Value, HeldModifiers(), pressed, manager);
}
