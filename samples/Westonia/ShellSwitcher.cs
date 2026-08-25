using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using Westonia.Shell;

namespace Westonia;

internal sealed class ShellSwitcher : IDisposable
{
    private const int EntryHeight = 30;
    private const int Width = 320;
    private const int Padding = 18;

    private readonly AvaloniaUIHost _host;
    private readonly ShellLayers _layers;
    private readonly WestonShell _shell;
    private readonly UISurfaceIndex _surfaces;
    private readonly SwitcherModel _model = new();
    private readonly List<ShellWindow> _order = [];
    private AvaloniaUISurface? _surface;
    private UISurfaceNode? _node;
    private int _index;
    private bool _disposed;

    public ShellSwitcher(AvaloniaUIHost host, ShellLayers layers, WestonShell shell, UISurfaceIndex index)
    {
        _host = host;
        _layers = layers;
        _shell = shell;
        _surfaces = index;
    }

    public bool IsOpen { get; private set; }

    public Func<Box>? Area { get; set; }

    public Func<double>? Scale { get; set; }

    public Action? Changed { get; set; }

    public void Open()
    {
        if (_disposed)
        {
            return;
        }

        if (IsOpen)
        {
            Next();
            return;
        }

        _order.Clear();
        foreach (var window in _shell.Windows)
        {
            if (window.Kind != ShellWindowKind.Minimized && window.Window.IsMapped)
            {
                _order.Add(window);
            }
        }

        if (_order.Count < 2)
        {
            return;
        }

        _model.Entries.Clear();
        foreach (var window in _order)
        {
            _model.Entries.Add(new SwitcherEntry(
                window.Window.Title is { Length: > 0 } title ? title : window.Window.AppId));
        }

        var height = (_order.Count * EntryHeight) + Padding;
        _surface = _host.CreateSurface(new UISurfaceOptions
        {
            Target = _host.Produces,
            Width = Width,
            Height = height,
            Scale = Scale?.Invoke() ?? 1.0,
        }) as AvaloniaUISurface;
        if (_surface is null)
        {
            return;
        }

        _surface.Content = new SwitcherView { DataContext = _model };
        _node = new UISurfaceNode(_layers.Panel, _surface, _surfaces) { PreciseDamage = true };

        var area = Area?.Invoke() ?? new Box(0, 0, 1280, 720);
        var x = area.X + ((area.Width - Width) / 2);
        var y = area.Y + ((area.Height - height) / 2);
        _surface.SetPosition(x, y);
        _node.SetPosition(x, y);

        IsOpen = true;
        _index = 1;
        Highlight();
    }

    public void Next()
    {
        if (!IsOpen)
        {
            return;
        }

        _index = (_index + 1) % _order.Count;
        Highlight();
    }

    public void Previous()
    {
        if (!IsOpen)
        {
            return;
        }

        _index = (_index - 1 + _order.Count) % _order.Count;
        Highlight();
    }

    public void Commit()
    {
        if (!IsOpen)
        {
            return;
        }

        var chosen = _index >= 0 && _index < _order.Count ? _order[_index] : null;
        Close();
        if (chosen is not null)
        {
            _shell.Focus(chosen);
        }
    }

    public void Cancel() => Close();

    public void Dispose()
    {
        _disposed = true;
        Close();
    }

    private void Close()
    {
        IsOpen = false;
        _node?.Dispose();
        _node = null;
        _surface?.Dispose();
        _surface = null;
        _order.Clear();
        _model.Entries.Clear();
        Changed?.Invoke();
    }

    private void Highlight()
    {
        for (var i = 0; i < _model.Entries.Count; i++)
        {
            _model.Entries[i].Selected = i == _index;
        }

        Changed?.Invoke();
    }
}
