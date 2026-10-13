using Basin;
using Basin.Diagnostics;
using Basin.Hypr;
using Basin.Eis;
using Basin.Hypr.InputCapture;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private Basin.Hypr.PreferredTriggerShortcuts _shortcuts = null!;
    private HyprCtm? _hyprCtm;
    private InputCaptureEngine? _inputCapture;

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
        _inputCapture = _services.Find<InputCaptureEngine>();
        if (_inputCapture is { } capture)
        {
            capture.WarpRequested += (x, y) => MoveCursor(x, y, (uint)Environment.TickCount);
        }
    }

    private bool CaptureMotion(uint time, double x, double y, double dx, double dy) =>
        _inputCapture is { } capture && capture.NotifyMotion(time, x, y, dx, dy);

    private bool HandleGlobalShortcut(uint key, bool pressed) =>
        !_sessionLock.IsLocked
        && _shortcuts.HandleKey(key, _seat.Keyboard.RawKeysymFor(key).Value, HeldModifiers(), pressed);

    private void ConfigureShortcuts(Config config)
    {
        var bindings = config.Bindings;
        _shortcuts.IsTaken = (keysym, modifiers) =>
        {
            foreach (var binding in bindings)
            {
                if (binding.Keysym == keysym && binding.ModifierMask == modifiers)
                {
                    return true;
                }
            }

            return false;
        };
        _shortcuts.Overrides = config.Shortcuts;
        foreach (var (key, _) in config.Shortcuts)
        {
            if (!_shortcuts.IsRegistered(key.AppId, key.Id))
            {
                _log.Debug($"[shortcuts] {key.AppId}:{key.Id} is bound but no client has registered it");
            }
        }
    }
}
