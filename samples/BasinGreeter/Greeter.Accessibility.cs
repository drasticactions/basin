using Avalonia.Media;
using Basin.Diagnostics;
using BasinGreeter.UI;

namespace BasinGreeter;

internal sealed partial class Greeter
{
    private double _textScale = 1.0;
    private bool _highContrast;

    private void WireAccessibility()
    {
        if (Avalonia.Application.Current is { } app)
        {
            HighContrastTheme.Install(app);
        }

        var model = _model.Accessibility;
        model.HighContrastChanged += SetHighContrast;
        model.TextScaleChanged += SetTextScale;
        ApplyAccessibilityDefaults();
        _greeter.StartAccessibility();
    }

    private void ApplyAccessibilityDefaults()
    {
        _model.Accessibility.Show(_config.Settings.HighContrast, _config.Settings.TextScale);
        SetHighContrast(_config.Settings.HighContrast);
        SetTextScale(_config.Settings.TextScale);
    }

    internal void SetHighContrast(bool on)
    {
        _model.Accessibility.Show(on, _textScale);
        if (_highContrast == on && Avalonia.Application.Current?.RequestedThemeVariant == (on ? HighContrastTheme.Variant : null))
        {
            return;
        }

        _highContrast = on;
        if (Avalonia.Application.Current is { } app)
        {
            app.RequestedThemeVariant = on ? HighContrastTheme.Variant : null;
        }

        ApplyLook();
        foreach (var (_, model) in _backgrounds.Values)
        {
            model.Fill = new SolidColorBrush(on ? Colors.Black : Argb(_config.BackgroundColor));
            model.Image = on ? null : _backgroundImage;
        }

        BasinReport.Line($"CONTRAST {(on ? "on" : "off")}");
        _outputs.ScheduleAll();
    }

    internal void SetTextScale(double scale)
    {
        scale = Math.Clamp(scale, 1.0, 2.0);
        _model.Accessibility.Show(_highContrast, scale);
        if (Math.Abs(_textScale - scale) < 0.001 && _box.Zoom == scale)
        {
            return;
        }

        _textScale = scale;
        _greeter.TextScale = scale;
        LayoutKeyboard();
        BasinReport.Line($"TEXTSCALE {AccessibilityModel.SizeOf(scale)}");
    }
}
