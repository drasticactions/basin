using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace BasinGreeter.UI;

public static class HighContrastTheme
{
    public static readonly ThemeVariant Variant = new("HighContrast", ThemeVariant.Dark);

    public static readonly Color Focus = Color.FromRgb(0xff, 0xff, 0x00);

    public static void Install(Application app)
    {
        var black = new SolidColorBrush(Colors.Black);
        var white = new SolidColorBrush(Colors.White);
        var yellow = new SolidColorBrush(Focus);
        var dictionary = new ResourceDictionary
        {
            ["GreeterBox"] = black,
            ["GreeterBoxBorder"] = white,
            ["GreeterError"] = yellow,
            ["GreeterMuted"] = white,
            ["GreeterAvatar"] = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)),
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
            ["ComboBoxBackground"] = black,
            ["ComboBoxForeground"] = white,
            ["ComboBoxBorderBrush"] = white,
            ["ListBoxItemBackgroundSelected"] = yellow,
            ["ListBoxItemForegroundSelected"] = black,
            ["ListBoxItemBackgroundSelectedPointerOver"] = yellow,
            ["ListBoxItemForegroundSelectedPointerOver"] = black,
            ["AccentButtonBackground"] = yellow,
            ["AccentButtonBackgroundPointerOver"] = yellow,
            ["AccentButtonBackgroundPressed"] = white,
            ["AccentButtonForeground"] = black,
            ["AccentButtonForegroundPointerOver"] = black,
            ["AccentButtonForegroundPressed"] = black,
            ["AccentButtonBorderBrush"] = yellow,
            ["SystemControlFocusVisualPrimaryBrush"] = yellow,
            ["SystemControlFocusVisualSecondaryBrush"] = black,
        };
        app.Resources.ThemeDictionaries[Variant] = dictionary;
    }
}
