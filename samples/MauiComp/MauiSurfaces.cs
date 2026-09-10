using Basin.UI.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using AC = Avalonia.Controls;
using M = Microsoft.Maui.Controls;

namespace MauiComp;

internal sealed class MauiSurfaces
{
    private readonly ShellAvaloniaApp _app;

    public MauiSurfaces(ShellAvaloniaApp app)
    {
        _app = app;
    }

    public int Live { get; private set; }

    public MauiScope Attach(AvaloniaUISurface surface, M.Page page)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(page);

        var scope = _app.Context.Services.CreateScope();
        try
        {
            var context = new MauiContext(scope.ServiceProvider);
            foreach (var service in context.Services.GetServices<IMauiInitializeScopedService>())
            {
                service.Initialize(context.Services);
            }

            var mauiApp = (ShellMauiApp)_app.Application;
            mauiApp.Pending = page;
            var window = (M.Window)_app.Application.CreateWindow(new ActivationState(context));
            if (!ReferenceEquals(window.Page, page))
            {
                throw new InvalidOperationException("MAUI created a window for a page other than the one asked for.");
            }

            var control = window.ToPlatform(context) as AC.Control
                ?? throw new InvalidOperationException("The MAUI window handler did not return a control.");
            surface.Content = control;
            Live++;
            return new MauiScope(scope, window, control, surface, () => Live--);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
