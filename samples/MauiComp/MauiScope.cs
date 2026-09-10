using Basin.UI.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using AC = Avalonia.Controls;
using M = Microsoft.Maui.Controls;

namespace MauiComp;

internal sealed class MauiScope : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly AvaloniaUISurface _surface;
    private readonly Action _released;
    private bool _destroyed;
    private bool _disposed;

    internal MauiScope(
        IServiceScope scope, M.Window window, AC.Control control, AvaloniaUISurface surface, Action released)
    {
        _scope = scope;
        Window = window;
        Control = control;
        _surface = surface;
        _released = released;
        window.Destroying += (_, _) => _destroyed = true;
    }

    public M.Window Window { get; }

    public AC.Control Control { get; }

    public M.Page? Page => Window.Page;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ReferenceEquals(_surface.Content, Control))
        {
            _surface.Content = null;
        }

        if (!_destroyed)
        {
            ((IWindow)Window).Destroying();
        }

        Window.Handler?.DisconnectHandler();
        (Control as IDisposable)?.Dispose();
        _scope.Dispose();
        _released();
    }
}
