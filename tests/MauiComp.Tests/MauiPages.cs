using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using M = Microsoft.Maui.Controls;

namespace MauiComp.Tests;

internal static class MauiPages
{
    public static ShellAvaloniaApp App => (ShellAvaloniaApp)Application.Current!;

    public static (Control Control, M.Window Window, IServiceScope Scope) Realize(M.Page page)
    {
        var app = App;
        var scope = app.Context.Services.CreateScope();
        var context = new MauiContext(scope.ServiceProvider);
        foreach (var service in context.Services.GetServices<IMauiInitializeScopedService>())
        {
            service.Initialize(context.Services);
        }

        ((ShellMauiApp)app.Application).Pending = page;
        var window = (M.Window)app.Application.CreateWindow(new ActivationState(context));
        var control = (Control)window.ToPlatform(context);
        return (control, window, scope);
    }

    public static IEnumerable<T> Descendants<T>(M.Element root)
        where T : M.Element
    {
        foreach (var child in ((Microsoft.Maui.IVisualTreeElement)root).GetVisualChildren())
        {
            if (child is T match)
            {
                yield return match;
            }

            if (child is M.Element element)
            {
                foreach (var nested in Descendants<T>(element))
                {
                    yield return nested;
                }
            }
        }
    }

    public static Control Layout(M.Page page, double width, double height)
    {
        var (control, _, _) = Realize(page);
        var root = new Window
        {
            Width = width,
            Height = height,
            Content = control,
        };

        root.Show();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        return control;
    }
}
