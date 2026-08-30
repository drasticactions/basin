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

    private static int SplitX(Workspace workspace) =>
        workspace.TileArea.X + (int)(workspace.TileArea.Width * workspace.SplitFraction);

    internal void TileWindows()
    {
        if (CurrentWorkspace() is not { } workspace)
        {
            return;
        }

        workspace.Tiled.Clear();
        foreach (var window in _windows)
        {
            if (window.Workspace == workspace && !window.Minimized && workspace.Tiled.Count < 2)
            {
                workspace.Tiled.Add(window);
            }
        }

        if (workspace.Tiled.Count < 2)
        {
            workspace.Tiled.Clear();
            BasinReport.Line($"TILE needs two windows");
            return;
        }

        var view = Views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output) ?? Views[0];
        var origin = _layout.BoxOf(view.Output);
        var usable = view.UsableArea.IsEmpty ? origin with { X = 0, Y = 0 } : view.UsableArea;
        workspace.TileArea = new Box(origin.X + usable.X, origin.Y + usable.Y, usable.Width, usable.Height);
        workspace.SplitFraction = 0.5;
        ApplySplit(workspace);
        BasinReport.Line($"TILED {workspace.Tiled[0].Toplevel.AppId} | {workspace.Tiled[1].Toplevel.AppId} transactions={_useTransactions}");
    }

    internal void SetSplit(double fraction)
    {
        if (CurrentWorkspace() is not { Tiled.Count: 2 } workspace)
        {
            BasinReport.Line($"SPLIT needs a tiled pair");
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

    private void ApplySplit(Workspace workspace)
    {
        if (workspace.Tiled.Count != 2)
        {
            return;
        }

        var left = workspace.Tiled[0];
        var right = workspace.Tiled[1];
        var splitX = SplitX(workspace);
        var leftWidth = Math.Max(32, splitX - workspace.TileArea.X);
        var rightWidth = Math.Max(32, workspace.TileArea.Right - splitX);

        if (!_useTransactions)
        {
            left.MoveTo(workspace.TileArea.X, workspace.TileArea.Y);
            left.Toplevel.SetSize(leftWidth, workspace.TileArea.Height);
            right.MoveTo(splitX, workspace.TileArea.Y);
            right.Toplevel.SetSize(rightWidth, workspace.TileArea.Height);
            return;
        }

        DropSplitTransaction();
        var transaction = new Transaction(_loop);
        _splitTransaction = transaction;
        _splitWorkspace = workspace;

        FreezeForSplit(left);
        FreezeForSplit(right);

        left.MoveTo(workspace.TileArea.X, workspace.TileArea.Y);
        left.Toplevel.SetSize(leftWidth, workspace.TileArea.Height);
        left.Toplevel.SendConfigure(transaction);

        right.MoveTo(splitX, workspace.TileArea.Y);
        right.Toplevel.SetSize(rightWidth, workspace.TileArea.Height);
        right.Toplevel.SendConfigure(transaction);

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

    private void FreezeForSplit(Window window)
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
