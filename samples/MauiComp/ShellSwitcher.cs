using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using MauiComp.Shell;

namespace MauiComp;

internal sealed class ShellSwitcher : IDisposable
{
    private const int EntryHeight = 30;
    private const int Width = 320;
    private const int Padding = 16;

    private readonly AvaloniaUIHost _host;
    private readonly MauiSurfaces _surfaces;
    private readonly SceneTree _layer;
    private readonly UISurfaceIndex _index;
    private readonly SwitcherModel _model = new();
    private readonly List<ShellWindow> _order = [];
    private AvaloniaUISurface? _surface;
    private UISurfaceNode? _node;
    private MauiScope? _scope;
    private int _selected;
    private bool _disposed;

    public ShellSwitcher(AvaloniaUIHost host, MauiSurfaces surfaces, SceneTree layer, UISurfaceIndex index)
    {
        _host = host;
        _surfaces = surfaces;
        _layer = layer;
        _index = index;
    }

    public bool IsOpen { get; private set; }

    public Func<Box>? Area { get; set; }

    public Func<double>? Scale { get; set; }

    public Action? Changed { get; set; }

    public Action<ShellWindow>? Chosen { get; set; }

    public void Open(IEnumerable<ShellWindow> windows)
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
        _order.AddRange(windows);
        if (_order.Count < 2)
        {
            return;
        }

        _model.Entries.Clear();
        foreach (var window in _order)
        {
            _model.Entries.Add(new SwitcherEntry(window.Label));
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

        _scope = _surfaces.Attach(_surface, new SwitcherPage { BindingContext = _model });
        _node = new UISurfaceNode(_layer, _surface, _index) { PreciseDamage = true };

        var area = Area?.Invoke() ?? new Box(0, 0, 1280, 720);
        var x = area.X + ((area.Width - Width) / 2);
        var y = area.Y + ((area.Height - height) / 2);
        _surface.SetPosition(x, y);
        _node.SetPosition(x, y);

        IsOpen = true;
        _selected = 1;
        Highlight();
    }

    public void Next()
    {
        if (!IsOpen)
        {
            return;
        }

        _selected = (_selected + 1) % _order.Count;
        Highlight();
    }

    public void Previous()
    {
        if (!IsOpen)
        {
            return;
        }

        _selected = (_selected - 1 + _order.Count) % _order.Count;
        Highlight();
    }

    public void Commit()
    {
        if (!IsOpen)
        {
            return;
        }

        var chosen = _selected >= 0 && _selected < _order.Count ? _order[_selected] : null;
        Close();
        if (chosen is not null)
        {
            Chosen?.Invoke(chosen);
        }
    }

    public void Cancel() => Close();

    public void Forget(ShellWindow window)
    {
        if (IsOpen && _order.Contains(window))
        {
            Close();
        }
    }

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
        _scope?.Dispose();
        _scope = null;
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
            _model.Entries[i].Selected = i == _selected;
        }

        Changed?.Invoke();
    }
}
