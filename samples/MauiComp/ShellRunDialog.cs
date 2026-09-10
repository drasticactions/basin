using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using MauiComp.Shell;

namespace MauiComp;

internal sealed class ShellRunDialog : IDisposable
{
    public const int Width = 360;
    public const int Height = 150;

    private readonly AvaloniaUIHost _host;
    private readonly MauiSurfaces _surfaces;
    private readonly SceneTree _layer;
    private readonly UISurfaceIndex _index;
    private readonly RunModel _model;
    private AvaloniaUISurface? _surface;
    private UISurfaceNode? _node;
    private MauiScope? _scope;
    private int _x;
    private int _y;
    private bool _disposed;

    public ShellRunDialog(
        AvaloniaUIHost host,
        MauiSurfaces surfaces,
        SceneTree layer,
        UISurfaceIndex index,
        Action<string> run)
    {
        _host = host;
        _surfaces = surfaces;
        _layer = layer;
        _index = index;
        _model = new RunModel(
            command =>
            {
                Close();
                if (command.Trim() is { Length: > 0 } trimmed)
                {
                    run(trimmed);
                }
            },
            Close);
    }

    public bool IsOpen => _surface is not null;

    public IUISurface? Surface => _surface;

    public string Text => _model.Command;

    public int X => _x;

    public int Y => _y;

    public bool OwnsSurface(IUISurface? surface) => surface is not null && ReferenceEquals(surface, _surface);

    public bool IsDragHandleAt(double x, double y) =>
        IsOpen && x >= _x && x < _x + Width - 25 && y >= _y && y < _y + 24;

    public void MoveTo(int x, int y)
    {
        if (!IsOpen)
        {
            return;
        }

        _x = x;
        _y = y;
        _surface!.SetPosition(x, y);
        _node!.SetPosition(x, y);
        Changed?.Invoke();
    }

    public Func<Box>? Area { get; set; }

    public Func<double>? Scale { get; set; }

    public Action<IUISurface?>? FocusChanged { get; set; }

    public Action? Changed { get; set; }

    public void Open()
    {
        if (_disposed || IsOpen)
        {
            return;
        }

        _surface = _host.CreateSurface(new UISurfaceOptions
        {
            Target = _host.Produces,
            Width = Width,
            Height = Height,
            Scale = Scale?.Invoke() ?? 1.0,
        }) as AvaloniaUISurface;
        if (_surface is null)
        {
            return;
        }

        _model.Command = string.Empty;
        var page = new RunPage { BindingContext = _model };
        _scope = _surfaces.Attach(_surface, page);
        _node = new UISurfaceNode(_layer, _surface, _index) { PreciseDamage = true };

        var area = Area?.Invoke() ?? new Box(0, 0, 1280, 720);
        _x = area.X + ((area.Width - Width) / 2);
        _y = area.Y + ((area.Height - ShellOutputs.PanelThickness - Height) / 2);
        _surface.SetPosition(_x, _y);
        _node.SetPosition(_x, _y);
        FocusChanged?.Invoke(_surface);
        page.FocusEntry();
        Changed?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        FocusChanged?.Invoke(null);
        _node?.Dispose();
        _node = null;
        _scope?.Dispose();
        _scope = null;
        _surface?.Dispose();
        _surface = null;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
    }
}
