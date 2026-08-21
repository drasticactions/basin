using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Avalonia;

public sealed class DragIconWindow : Window
{
    private readonly BasinToplevelView _view;
    private bool _closing;

    internal DragIconWindow(ToplevelWindows manager, int id, int width, int height)
    {
        _view = new BasinToplevelView(manager.Host, _ => manager.CreateView(id));
        var background = new global::Avalonia.Controls.Panel
        {
            Background = global::Avalonia.Media.Brushes.Transparent,
        };
        background.Children.Add(_view);
        Content = background;
        WindowDecorations = WindowDecorations.None;
        Background = global::Avalonia.Media.Brushes.Transparent;
        TransparencyLevelHint = [global::Avalonia.Controls.WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.Manual;
        Width = width;
        Height = height;
        Closing += (_, e) =>
        {
            if (!_closing)
            {
                e.Cancel = true;
            }
        };
    }

    internal void PlaceAt(global::Avalonia.PixelPoint screen) => Position = screen;

    internal async Task CloseFromCompositorAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        await _view.ShutdownAsync();
        Close();
    }
}
