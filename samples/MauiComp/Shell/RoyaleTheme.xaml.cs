using Microsoft.Maui.Controls;

namespace MauiComp.Shell;

public partial class RoyaleTheme : ResourceDictionary
{
    public RoyaleTheme()
    {
        InitializeComponent();
    }

    public static Brush Brush(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.Maui.Graphics.Colors.Magenta);
}
