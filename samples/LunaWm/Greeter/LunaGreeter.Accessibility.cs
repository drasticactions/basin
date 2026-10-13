using Basin;
using Basin.Diagnostics;
using Basin.UI.Avalonia;

namespace LunaWm;

internal sealed partial class LunaGreeter
{
    private ScreenReader? _reader => _greeter.Reader;
    private double _textScale = 1.0;
    private bool _highContrast;

    private void WireAccessibility()
    {
        _reader!.Changed += () =>
        {
            if (_menu is { Open: true, Kind: "device" })
            {
                FillMenu(rebuild: true);
            }
        };
        ApplyAccessibilityDefaults();
        _greeter.StartAccessibility();
    }

    private void ApplyAccessibilityDefaults()
    {
        SetHighContrast(_config.Settings.HighContrast);
        SetTextScale(_config.Settings.TextScale);
    }

    internal void SetHighContrast(bool on)
    {
        if (_highContrast == on && _model.HighContrast == on)
        {
            return;
        }

        _highContrast = on;
        _model.HighContrast = on;
        foreach (var (_, model) in _backgrounds.Values)
        {
            model.HighContrast = on;
        }

        BasinReport.Line($"CONTRAST {(on ? "on" : "off")}");
        _outputs.ScheduleAll();
    }

    internal void SetTextScale(double scale)
    {
        scale = Math.Clamp(scale, 1.0, 2.0);
        if (Math.Abs(_textScale - scale) < 0.001 && _box.Zoom == scale)
        {
            return;
        }

        _textScale = scale;
        _greeter.TextScale = scale;
        LayoutKeyboard();
        BasinReport.Line($"TEXTSCALE {SizeOf(scale)}");
    }
}
