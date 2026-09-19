using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Tarn;

public sealed class TarnApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }

    private MainView? _view;

    public MainView? View => _view;

    public static string? StartupEndpoint { get; set; }

    public static bool AutoConnect { get; set; }

    public static Basin.Capabilities.IVideoDecoder? VideoDecoder { get; set; }

    public static bool CarriesDmabuf { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            case ISingleViewApplicationLifetime single:
                _view = new MainView();
                single.MainView = _view;
                break;
            case IClassicDesktopStyleApplicationLifetime desktop:
                _view = new MainView();
                desktop.MainWindow = new Window
                {
                    Title = "Tarn",
                    Width = 1280,
                    Height = 800,
                    Content = _view,
                };
                break;
        }

        if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            activatable.Deactivated += (_, e) => OnActivation(e.Kind, active: false);
            activatable.Activated += (_, e) => OnActivation(e.Kind, active: true);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnActivation(ActivationKind kind, bool active)
    {
        if (kind != ActivationKind.Background || _view is not { } view)
        {
            return;
        }

        if (active)
        {
            view.Resume();
        }
        else
        {
            view.Suspend();
        }
    }
}
