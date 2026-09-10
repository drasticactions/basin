using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using MauiComp.Shell;
using M = Microsoft.Maui.Controls;

namespace MauiComp;

internal sealed class ShellStartMenu : IDisposable
{
    public const int Width = 210;
    public const int Height = 100;

    private readonly AvaloniaUIHost _host;
    private readonly MauiSurfaces _surfaces;
    private readonly SceneTree _layer;
    private readonly UISurfaceIndex _index;
    private readonly StartMenuModel _model;
    private AvaloniaUISurface? _surface;
    private UISurfaceNode? _node;
    private MauiScope? _scope;
    private StartMenuPage? _page;
    private bool _disposed;

    public ShellStartMenu(
        AvaloniaUIHost host,
        MauiSurfaces surfaces,
        SceneTree layer,
        UISurfaceIndex index,
        Action run,
        Action exit)
    {
        _host = host;
        _surfaces = surfaces;
        _layer = layer;
        _index = index;
        _model = new StartMenuModel(ShowPrograms, run, exit);
    }

    public bool IsOpen => _surface is not null;

    public bool IsProgramsOpen => _page?.IsProgramsOpen == true;

    public IUISurface? Surface => _surface;

    public Func<Box>? Area { get; set; }

    public Func<double>? Scale { get; set; }

    public Action? Changed { get; set; }

    public Action<string>? Launch { get; set; }

    public Func<IReadOnlyList<AppEntry>>? Programs { get; set; }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

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

        _page = new StartMenuPage { BindingContext = _model, Programs = BuildPrograms() };
        _scope = _surfaces.Attach(_surface, _page);
        _node = new UISurfaceNode(_layer, _surface, _index) { PreciseDamage = true };

        var area = Area?.Invoke() ?? new Box(0, 0, 1280, 720);
        var x = area.X;
        var y = area.Y + area.Height - ShellOutputs.PanelThickness - Height;
        _surface.SetPosition(x, y);
        _node.SetPosition(x, y);
        Changed?.Invoke();
    }

    public void ShowPrograms()
    {
        if (_page is null)
        {
            return;
        }

        var area = Area?.Invoke() ?? new Box(0, 0, 1280, 720);
        _page.ShowPrograms(Math.Min(area.Height - ShellOutputs.PanelThickness - 2, ProgramsRows * ProgramsRowHeight));
    }

    private const int ProgramsRows = 20;
    private const int ProgramsRowHeight = 26;

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        _page?.HidePrograms();
        _node?.Dispose();
        _node = null;
        _scope?.Dispose();
        _scope = null;
        _surface?.Dispose();
        _surface = null;
        _page = null;
        Changed?.Invoke();
    }

    public bool OwnsSurface(IUISurface? surface) => surface is not null && ReferenceEquals(surface, _surface);

    public void Dispose()
    {
        _disposed = true;
        Close();
    }

    private M.MenuFlyout BuildPrograms()
    {
        var flyout = new M.MenuFlyout();
        var entries = Programs?.Invoke() ?? [];
        foreach (var entry in entries)
        {
            var item = new M.MenuFlyoutItem { Text = entry.Name };
            var command = DesktopEntries.CommandLine(entry.Exec);
            item.Clicked += (_, _) =>
            {
                Close();
                Launch?.Invoke(command);
            };
            flyout.Add(item);
        }

        if (entries.Count == 0)
        {
            flyout.Add(new M.MenuFlyoutItem { Text = "(Empty)", IsEnabled = false });
        }

        return flyout;
    }
}
