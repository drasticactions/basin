namespace MauiComp;

internal sealed partial class MauiComp
{
    private MauiCompSeat? _seat;

    private void WireSeat()
    {
        _seat = new MauiCompSeat(_host, Seat, _layout, _scene, _cursor, _shellSurfaces, _input, _inputSink, _log)
        {
            KeyHook = OnKeyBinding,
            ButtonHook = OnButton,
            ChromeClick = OnChromeClick,
            ChromeCursor = ChromeCursorAt,
            Grabbing = () => IsGrabbing,
            Configurator = _inputConfigurator,
        };
        _seat.Binder.RelativePointer = _services.Find<Basin.Desktop.RelativePointerManager>();
        _seat.Binder.Constraints = _services.Find<Basin.Desktop.PointerConstraintDriver>();
        _inputConfigurator.Rejected += outcome =>
            _log.Warn($"[input] {InputSettingNamesKey(outcome)} on {outcome.DeviceName ?? "every device"}: {outcome.Result}");
        _inputConfigurator.Apply(_config.InputRules);
        _inputConfigurator.Attach(Seat, _seat.Binder, _input?.Configuration);
        _seat.PointerMoved = (x, y) =>
        {
            if (IsGrabbing)
            {
                ContinueInteractive(x, y);
            }
        };
    }

    private static string InputSettingNamesKey(Basin.Capabilities.InputRuleOutcome outcome) =>
        Basin.Capabilities.InputSettingNames.Key(outcome.Setting);

    private bool OnButton(uint time, uint button, bool pressed)
    {
        if (!pressed)
        {
            if (IsGrabbing)
            {
                ResetCursorMode();
            }

            return false;
        }

        if (_seat is null || _scene.SurfaceAt(_seat.PointerX, _seat.PointerY) is not { Surface: { } surface })
        {
            return false;
        }

        for (var candidate = surface; candidate is not null; candidate = candidate.SubsurfaceRole?.Parent)
        {
            foreach (var window in _windows)
            {
                if (ReferenceEquals(candidate, window.Window.Surface))
                {
                    Focus(window);
                    return false;
                }
            }
        }

        return false;
    }
}
