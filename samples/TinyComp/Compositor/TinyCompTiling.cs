using System.Diagnostics;
using Basin;
using Basin.Host;
using Basin.Backend.Libinput;
using Basin.Cli;
using Basin.Effects;
using Basin.Backend.Wayland;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Capabilities;
using Basin.UI.Skia;
using Wayland;
using Wayland.Server;

using Basin.Diagnostics;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private SceneRenderOptions SceneOptions(IOutput output) => new()
    {
        Background = Background,
        Projection = OutputProjection.For(output),
    };

    private bool _useTransactions;
    private Transaction? _splitTransaction;
    private readonly List<SceneSnapshot> _splitSnapshots = [];

    internal interface ITiledWindow : IGrabTarget
    {
        Workspace? Workspace { get; }

        bool Minimized { get; }

        bool IsFullscreen { get; }

        string AppId { get; }

        SceneTree? Tree { get; }

        SceneSurface? SceneSurface { get; }

        Basin.Desktop.FullscreenRoundTrip RoundTrip { get; }

        void EnterFullscreen();

        void FloatedOutOfFullscreen();

        void Tile(Box slot, Transaction? transaction);
    }

    private static int SplitX(Workspace workspace) =>
        workspace.TileArea.X + (int)(workspace.TileArea.Width * workspace.SplitFraction);

    internal void TileWindows()
    {
        if (CurrentWorkspace() is not { } workspace)
        {
            return;
        }

        workspace.Tiled.Clear();
        foreach (var window in _windows.Cast<ITiledWindow>().Concat(_xwindows.Where(x => x.Framable)))
        {
            if (window.Workspace == workspace && !window.Minimized && workspace.Tiled.Count < 2)
            {
                workspace.Tiled.Add(window);
            }
        }

        if (workspace.Tiled.Count < 2)
        {
            workspace.Tiled.Clear();
            _report.Line($"TILE needs two windows");
            return;
        }

        var view = Views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output) ?? Views[0];
        var origin = _layout.BoxOf(view.Output);
        var usable = FlatArea(view, view.UsableArea.IsEmpty ? origin with { X = 0, Y = 0 } : view.UsableArea);
        workspace.TileArea = new Box(origin.X + usable.X, origin.Y + usable.Y, usable.Width, usable.Height);
        workspace.SplitFraction = 0.5;
        ApplySplit(workspace);
        _report.Line($"TILED {workspace.Tiled[0].AppId} | {workspace.Tiled[1].AppId} transactions={_useTransactions}");
    }

    internal void SetSplit(double fraction)
    {
        if (CurrentWorkspace() is not { Tiled.Count: 2 } workspace)
        {
            _report.Line($"SPLIT needs a tiled pair");
            return;
        }

        workspace.SplitFraction = Math.Clamp(fraction, 0.1, 0.9);
        ApplySplit(workspace);
    }

    private void BeginSplitDrag(Workspace workspace)
    {
        _mode = DragMode.Split;
        _grabWindow = null;
        _splitWorkspace = workspace;
        _seat.Pointer.NotifyClearFocus();
        foreach (var window in workspace.Tiled)
        {
            window.SetResizing(true);
        }
    }

    private void DragSplit(double x)
    {
        if (_splitWorkspace is not { Tiled.Count: 2 } workspace || workspace.TileArea.Width <= 0)
        {
            return;
        }

        workspace.SplitFraction = Math.Clamp((x - workspace.TileArea.X) / workspace.TileArea.Width, 0.1, 0.9);
        ApplySplit(workspace);
    }

    private void EndSplitDrag()
    {
        if (_splitWorkspace is not { } workspace)
        {
            return;
        }

        foreach (var window in workspace.Tiled)
        {
            window.SetResizing(false);
        }
    }

    internal Box? TileSlotOf(ITiledWindow window)
    {
        if (window.Workspace is not { Tiled.Count: 2 } workspace || !workspace.Tiled.Contains(window))
        {
            return null;
        }

        var splitX = SplitX(workspace);
        var area = workspace.TileArea;
        return ReferenceEquals(workspace.Tiled[0], window)
            ? new Box(area.X, area.Y, Math.Max(32, splitX - area.X), area.Height)
            : new Box(splitX, area.Y, Math.Max(32, area.Right - splitX), area.Height);
    }

    internal void RequestFullscreen(bool fullscreen)
    {
        if (_focused is { } window)
        {
            window.Toplevel.RaiseFullscreenRequest(fullscreen);
        }
        else if (_focusedX is { } xwindow)
        {
            xwindow.SetFullscreen(fullscreen);
        }
        else
        {
            _report.Line($"FULLSCREEN needs a focused window");
            return;
        }

        _report.Line($"FULLSCREEN {(fullscreen ? "on" : "off")}");
    }

    internal void ToggleFloat()
    {
        ITiledWindow? focused = _focused is not null ? _focused : _focusedX is { Framable: true } x ? x : null;
        if (focused is not { } window || CurrentWorkspace() is not { } workspace)
        {
            _report.Line($"FLOAT needs a focused window");
            return;
        }

        if (workspace.Tiled.Remove(window))
        {
            var (width, height) = window.GeometrySize;
            var size = window.RoundTrip.Float(window.IsFullscreen, new Box(window.X, window.Y, width, height));
            if (size == Basin.Desktop.FloatSize.Keep)
            {
                window.FloatedOutOfFullscreen();
            }
            _report.Line($"FLOATED {window.AppId} size={(size == Basin.Desktop.FloatSize.Keep ? "keep" : "policy")}");
            return;
        }

        if (workspace.Tiled.Count >= 2)
        {
            _report.Line($"TILE pair is full");
            return;
        }

        var refullscreen = window.RoundTrip.Tile(window.IsFullscreen);
        if (refullscreen)
        {
            window.EnterFullscreen();
        }

        workspace.Tiled.Add(window);
        if (workspace.Tiled.Count == 2)
        {
            ApplySplit(workspace);
        }

        _report.Line($"RETILED {window.AppId} fullscreen={refullscreen}");
    }

    private void ApplySplit(Workspace workspace)
    {
        if (workspace.Tiled.Count != 2)
        {
            return;
        }

        if (!_useTransactions)
        {
            foreach (var window in workspace.Tiled)
            {
                if (!window.IsFullscreen && TileSlotOf(window) is { } slot)
                {
                    window.Tile(slot, null);
                }
            }

            return;
        }

        DropSplitTransaction();
        var transaction = new Transaction(_loop);
        _splitTransaction = transaction;
        _splitWorkspace = workspace;

        foreach (var window in workspace.Tiled)
        {
            if (!window.IsFullscreen && TileSlotOf(window) is { } slot)
            {
                if (window is Window)
                {
                    FreezeForSplit(window);
                }

                window.Tile(slot, transaction);
            }
        }

        transaction.Completed += () =>
        {
            if (ReferenceEquals(_splitTransaction, transaction))
            {
                ThawAfterSplit();
                _splitTransaction = null;
                _loop.DeferDestroy(transaction);
            }
        };
        transaction.Seal();
    }

    private void FreezeForSplit(ITiledWindow window)
    {
        if (window.SceneSurface is not { } scene || window.Tree is null)
        {
            return;
        }

        var snapshot = SceneSnapshot.Capture(window.Tree, window.Workspace?.Tree ?? _layers.Windows);
        _splitSnapshots.Add(snapshot);
        window.Tree.Enabled = false;

        scene.SendFrameDone((uint)Environment.TickCount);
    }

    private void ThawAfterSplit()
    {
        foreach (var window in _splitWorkspace?.Tiled ?? [])
        {
            if (window.Tree is not null)
            {
                window.Tree.Enabled = !window.Minimized;
            }
        }

        foreach (var snapshot in _splitSnapshots)
        {
            _loop.DeferDestroy(snapshot);
        }

        _splitSnapshots.Clear();
    }

    private void DropSplitTransaction()
    {
        if (_splitTransaction is not { } outstanding)
        {
            return;
        }

        _splitTransaction = null;
        ThawAfterSplit();
        _loop.DeferDestroy(outstanding);
    }
}
