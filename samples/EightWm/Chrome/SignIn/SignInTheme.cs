using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace EightWm;

public static class SignInTheme
{
    public static readonly ThemeVariant HighContrast = new("HighContrast", ThemeVariant.Dark);

    public static readonly Color Focus = Color.FromRgb(0xff, 0xff, 0x00);

    public static void Install(Application app, Color accent)
    {
        ArgumentNullException.ThrowIfNull(app);
        var black = new SolidColorBrush(Colors.Black);
        var white = new SolidColorBrush(Colors.White);
        var yellow = new SolidColorBrush(Focus);
        app.Resources.ThemeDictionaries[ThemeVariant.Default] = new ResourceDictionary
        {
            ["SignInBackground"] = new SolidColorBrush(Darken(accent)),
            ["SignInTile"] = new SolidColorBrush(accent),
            ["SignInText"] = white,
            ["SignInError"] = new SolidColorBrush(Color.FromRgb(0xff, 0xd0, 0x40)),
            ["SignInFlyout"] = new SolidColorBrush(Color.FromRgb(0x1d, 0x1d, 0x1d)),
        };
        app.Resources.ThemeDictionaries[HighContrast] = new ResourceDictionary
        {
            ["SignInBackground"] = black,
            ["SignInTile"] = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)),
            ["SignInText"] = white,
            ["SignInError"] = yellow,
            ["SignInFlyout"] = black,
            ["TextControlBackground"] = black,
            ["TextControlBackgroundPointerOver"] = black,
            ["TextControlBackgroundFocused"] = black,
            ["TextControlForeground"] = white,
            ["TextControlForegroundFocused"] = white,
            ["TextControlBorderBrush"] = white,
            ["TextControlBorderBrushPointerOver"] = yellow,
            ["TextControlBorderBrushFocused"] = yellow,
            ["TextControlPlaceholderForeground"] = white,
            ["ButtonBackground"] = black,
            ["ButtonBackgroundPointerOver"] = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x00)),
            ["ButtonForeground"] = white,
            ["ButtonForegroundPointerOver"] = yellow,
            ["ButtonBorderBrush"] = white,
            ["ButtonBorderBrushPointerOver"] = yellow,
            ["ToggleSwitchFillOn"] = yellow,
            ["ToggleSwitchKnobFillOn"] = black,
            ["SystemControlFocusVisualPrimaryBrush"] = yellow,
            ["SystemControlFocusVisualSecondaryBrush"] = black,
        };
    }

    public static void SetHighContrast(Application app, bool on, ThemeVariant? normal)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.RequestedThemeVariant = on ? HighContrast : normal;
    }

    private static Color Darken(Color color) =>
        Color.FromRgb((byte)(color.R * 0.55), (byte)(color.G * 0.55), (byte)(color.B * 0.55));
}
