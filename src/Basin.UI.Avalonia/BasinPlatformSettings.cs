using Avalonia.Media;
using Avalonia.Platform;

namespace Basin.UI.Avalonia;

internal sealed class BasinPlatformSettings : DefaultPlatformSettings
{
    private UIThemeVariant _variant = UIThemeVariant.Light;
    private Color _accent = Color.FromRgb(0x30, 0x60, 0xA0);

    public UIThemeVariant Variant
    {
        get => _variant;
        set
        {
            if (_variant == value)
            {
                return;
            }

            _variant = value;
            OnColorValuesChanged(GetColorValues());
        }
    }

    public Color Accent
    {
        get => _accent;
        set
        {
            if (_accent == value)
            {
                return;
            }

            _accent = value;
            OnColorValuesChanged(GetColorValues());
        }
    }

    public override PlatformColorValues GetColorValues() => new()
    {
        ThemeVariant = _variant == UIThemeVariant.Dark
            ? PlatformThemeVariant.Dark
            : PlatformThemeVariant.Light,
        AccentColor1 = _accent,
    };
}
