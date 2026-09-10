using Avalonia;
using Avalonia.Controls.Maui.Platform;
using AM = Avalonia.Media;
using Avalonia.Fonts.Inter;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace MauiComp;

public sealed class ShellAvaloniaApp : MauiAvaloniaApplication
{
    public IMauiContext Context =>
        ApplicationContext ?? throw new InvalidOperationException("MAUI has not finished initializing.");

    public override void Initialize()
    {
        AvaloniaLocator.CurrentMutable.Bind<AM.FontManagerOptions>().ToConstant(new AM.FontManagerOptions
        {
            DefaultFamilyName = "fonts:Inter#Inter",
        });
        Avalonia.Media.FontManager.Current.AddFontCollection(new InterFontCollection());
        Styles.Add(new FluentTheme());
        var clearPage = new Avalonia.Styling.Style(x => x.OfType<Avalonia.Controls.ContentPage>());
        clearPage.Setters.Add(new Avalonia.Styling.Setter(
            Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, AM.Brushes.Transparent));
        Styles.Add(clearPage);
        Resources["MenuFlyoutPresenterBackground"] = new AM.SolidColorBrush(AM.Color.Parse("#FFFFFF"));
        Resources["MenuFlyoutPresenterBorderBrush"] = new AM.SolidColorBrush(AM.Color.Parse("#3C2A78"));
        Resources["MenuFlyoutPresenterBorderThemeThickness"] = new Avalonia.Thickness(1);
        Resources["MenuFlyoutItemBackgroundPointerOver"] = new AM.SolidColorBrush(AM.Color.Parse("#5433A8"));
        Resources["MenuFlyoutItemForegroundPointerOver"] = new AM.SolidColorBrush(AM.Color.Parse("#FFFFFF"));
        Resources["MenuFlyoutItemForeground"] = new AM.SolidColorBrush(AM.Color.Parse("#1E0E4C"));
        Resources["OverlayCornerRadius"] = new Avalonia.CornerRadius(0);
        Resources["MenuFlyoutThemeMinHeight"] = 22.0;
        Resources["MenuFlyoutItemThemePadding"] = new Avalonia.Thickness(24, 2, 24, 2);
        Resources["MenuFlyoutItemThemePaddingNarrow"] = new Avalonia.Thickness(24, 2, 24, 2);
        Resources["MenuFlyoutPresenterThemePadding"] = new Avalonia.Thickness(2);
        foreach (var key in new[]
        {
            "EntryBackground", "EntryBorderBrush", "EntryBorderBrushPointerOver", "EntryBorderBrushFocused",
        })
        {
            Resources[key] = AM.Brushes.Transparent;
        }
    }

    protected override MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<ShellMauiApp>().UseAvaloniaApp(useSingleViewLifetime: true);
        return builder.Build();
    }
}
