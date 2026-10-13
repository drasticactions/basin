using Avalonia;
using Basin.Diagnostics;

namespace EightWm;

internal sealed partial class EightGreeter
{
    private double _textScale = 1.0;
    private bool _highContrast;

    private void WireAccessibility()
    {
        _signIn.HighContrastChanged += SetHighContrast;
        _signIn.TextScaleChanged += SetTextScale;
        _signIn.NarratorChanged += on =>
        {
            if (on)
            {
                _greeter.Reader?.Start();
            }
            else
            {
                _greeter.Reader?.Stop();
            }
        };
        if (_greeter.Reader is { } reader)
        {
            reader.Changed += ShowReader;
        }

        ShowReader();
        ApplyAccessibilityDefaults();
        _greeter.StartAccessibility();
    }

    private void ShowReader()
    {
        var reader = _greeter.Reader;
        _signIn.NarratorAvailable = reader is { IsAvailable: true };
        _signIn.SetNarratorQuietly(reader is { IsRunning: true });
        _signIn.RevealAllowed = reader is not { IsRunning: true };
    }

    private void ApplyAccessibilityDefaults()
    {
        SetHighContrast(_config.Settings.HighContrast);
        SetTextScale(_config.Settings.TextScale);
    }

    internal void SetHighContrast(bool on)
    {
        _signIn.SetHighContrastQuietly(on);
        if (_highContrast == on && Application.Current?.RequestedThemeVariant == (on ? SignInTheme.HighContrast : NormalVariant))
        {
            return;
        }

        _highContrast = on;
        foreach (var curtain in Curtains())
        {
            curtain.HighContrast = on;
        }

        if (Application.Current is { } app)
        {
            SignInTheme.SetHighContrast(app, on, NormalVariant);
        }

        BasinReport.Line($"CONTRAST {(on ? "on" : "off")}");
        _outputs.ScheduleAll();
    }

    internal void SetTextScale(double scale)
    {
        scale = Math.Clamp(scale, 1.0, 2.0);
        _signIn.TextScale = scale;
        if (Math.Abs(_textScale - scale) < 0.001 && _box.Zoom == scale)
        {
            return;
        }

        _textScale = scale;
        _greeter.TextScale = scale;
        LayoutKeyboard();
        BasinReport.Line($"TEXTSCALE {scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");
    }
}
