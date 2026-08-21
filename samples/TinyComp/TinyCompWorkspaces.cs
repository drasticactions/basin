using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.Seat;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private ulong _workspaceIds;
    private Workspace? _splitWorkspace;
    private Workspace? _slidingFrom;
    private Workspace? _slidingTo;
    private OutputView? _slidingView;
    private const double SwipeTravel = 500;
    private const double SwipeFlingPerSecond = 1200;
    private const uint TouchSwipeFingers = 3;
    private const double TouchSwipeSlop = 24;
    private const double TouchFlingFraction = 0.2;
    private readonly SwipeRecognizer _swipe = new(fingers: 3);
    private OutputView? _swipeView;
    private Workspace? _swipeFrom;
    private Workspace? _swipeTo;
    private int _swipeDirection;
    private bool _swipeArmed;
    private bool _swipeVisual;
    private readonly TouchContacts _contacts = new();
    private TouchSwipeState _touchSwipe;
    private double _touchSwipeTravel;

    private enum TouchSwipeState
    {
        Idle,
        Watching,
        Claimed,
        Spent,
    }

    internal sealed class Workspace
    {
        public required ulong Id { get; init; }

        public required string Name { get; set; }

        public required string Handle { get; init; }

        public required SceneTree Tree { get; init; }

        public uint[] Coordinates { get; set; } = [];

        public bool Urgent { get; set; }

        public IGrabTarget? LastFocused { get; set; }

        public List<Window> Tiled { get; } = [];

        public double SplitFraction { get; set; } = 0.5;

        public Box TileArea { get; set; }
    }

    private sealed class WorkspacePolicy : IWorkspaceModel
    {
        private readonly TinyComp _comp;

        public WorkspacePolicy(TinyComp comp) => _comp = comp;

        private readonly WorkspaceObservers _observers = new();

        public void AddObserver(IWorkspaceObserver observer) => _observers.Add(observer);

        public void RemoveObserver(IWorkspaceObserver observer) => _observers.Remove(observer);

        internal void RaiseChanged() => _observers.Changed();

        internal void RaiseMembersChanged() => _observers.MembersChanged();

        public int EnumerateGroups(Span<WorkspaceGroupInfo> groups)
        {
            if (_comp._views.Count > groups.Length)
            {
                return -1;
            }

            for (var i = 0; i < _comp._views.Count; i++)
            {
                groups[i] = new WorkspaceGroupInfo(_comp._views[i].GroupId, ClientsCanCreateWorkspaces: true);
            }

            return _comp._views.Count;
        }

        public int EnumerateWorkspaces(ulong groupId, Span<WorkspaceInfo> workspaces)
        {
            if (_comp.ViewOfGroup(groupId) is not { } view)
            {
                return 0;
            }

            if (view.Workspaces.Count > workspaces.Length)
            {
                return -1;
            }

            for (var i = 0; i < view.Workspaces.Count; i++)
            {
                var workspace = view.Workspaces[i];
                var state = view.Active == workspace ? WorkspaceStateFlags.Active : WorkspaceStateFlags.None;
                if (workspace.Urgent)
                {
                    state |= WorkspaceStateFlags.Urgent;
                }
                workspaces[i] = new WorkspaceInfo(workspace.Id, workspace.Name, workspace.Handle, state, workspace.Coordinates);
            }

            return view.Workspaces.Count;
        }

        public int EnumerateMembers(ulong workspaceId, Span<WorkspaceMember> members)
        {
            if (_comp.FindWorkspace(workspaceId) is not { } found)
            {
                return 0;
            }

            var count = 0;
            foreach (var window in _comp._windows)
            {
                if (window.Workspace != found.Workspace || _comp.ToplevelIdOf(window.Toplevel.Surface) is not { } id)
                {
                    continue;
                }

                if (count == members.Length)
                {
                    return -1;
                }

                var (width, height) = window.GeometrySize;
                members[count++] = new WorkspaceMember(id, new Box(window.X, window.Y, width, height));
            }

            foreach (var xwindow in _comp._xwindows)
            {
                if (xwindow.Workspace != found.Workspace || _comp.ToplevelIdOf(xwindow.XWin.Surface) is not { } id)
                {
                    continue;
                }

                if (count == members.Length)
                {
                    return -1;
                }

                members[count++] = new WorkspaceMember(
                    id, new Box(xwindow.X, xwindow.Y, xwindow.XWin.Width, xwindow.XWin.Height));
            }

            return count;
        }

        public int EnumerateGroupOutputs(ulong groupId, Span<IOutput> outputs)
        {
            if (_comp.ViewOfGroup(groupId) is not { } view)
            {
                return 0;
            }

            if (outputs.Length < 1)
            {
                return -1;
            }

            outputs[0] = view.Output;
            return 1;
        }

        public bool Request(ulong targetId, in WorkspaceRequest request)
        {
            switch (request.Kind)
            {
                case WorkspaceRequestKind.Activate:
                    if (_comp.FindWorkspace(targetId) is { } activate)
                    {
                        _comp.ActivateWorkspace(activate.View, activate.Workspace);
                        return true;
                    }

                    return false;

                case WorkspaceRequestKind.Create:
                    if (_comp.ViewOfGroup(targetId) is { } view)
                    {
                        var created = _comp.CreateWorkspace(view, request.Name, afterActive: false);
                        if (request.ToplevelId != 0 &&
                            _comp.FindWindowByToplevelId(request.ToplevelId) is { } occupant)
                        {
                            _comp.MoveWindowToWorkspace(occupant, created);
                        }

                        return true;
                    }

                    return false;

                case WorkspaceRequestKind.Remove:
                    if (_comp.FindWorkspace(targetId) is { } remove &&
                        remove.View.Workspaces.Count > 1)
                    {
                        _comp.RemoveWorkspace(remove.View, remove.Workspace);
                        return true;
                    }

                    return false;

                case WorkspaceRequestKind.Move:
                    if (_comp.FindWorkspace(targetId) is { } destination &&
                        _comp.FindWindowByToplevelId(request.ToplevelId) is { } window)
                    {
                        _comp.MoveWindowToWorkspace(window, destination.Workspace);
                        return true;
                    }

                    return false;

                case WorkspaceRequestKind.Assign:
                    if (_comp.FindWorkspace(targetId) is { } assign &&
                        _comp.ViewOfGroup(request.GroupId) is { } target &&
                        target != assign.View &&
                        assign.View.Workspaces.Count > 1)
                    {
                        _comp.AssignWorkspace(assign.View, assign.Workspace, target);
                        return true;
                    }

                    return false;

                default:
                    return false;
            }
        }
    }

    private OutputView? ViewOfGroup(ulong groupId) => _views.FirstOrDefault(v => v.GroupId == groupId);

    private OutputView? ViewOf(Workspace workspace) => _views.FirstOrDefault(v => v.Workspaces.Contains(workspace));

    private (OutputView View, Workspace Workspace)? FindWorkspace(ulong id)
    {
        foreach (var view in _views)
        {
            foreach (var workspace in view.Workspaces)
            {
                if (workspace.Id == id)
                {
                    return (view, workspace);
                }
            }
        }

        return null;
    }

    private ulong? ToplevelIdOf(Surface? surface)
    {
        if (surface is null)
        {
            return null;
        }

        var infos = new ToplevelInfo[16];
        var count = _toplevels.Enumerate(infos);
        while (count < 0)
        {
            infos = new ToplevelInfo[infos.Length * 2];
            count = _toplevels.Enumerate(infos);
        }

        for (var i = 0; i < count; i++)
        {
            if (ReferenceEquals(infos[i].Surface, surface))
            {
                return infos[i].Id;
            }
        }

        return null;
    }

    private IGrabTarget? FindWindowByToplevelId(ulong toplevelId)
    {
        if (!_toplevels.TryGet(toplevelId, out var info) || info.Surface is not { } surface)
        {
            return null;
        }

        foreach (var window in _windows)
        {
            if (ReferenceEquals(window.Toplevel.Surface, surface))
            {
                return window;
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (ReferenceEquals(xwindow.XWin.Surface, surface))
            {
                return xwindow;
            }
        }

        return null;
    }

    private OutputView? ViewAt(double x, double y) =>
        _views.Count == 0 ? null : _views.FirstOrDefault(v => _layout.OutputAt(x, y) == v.Output) ?? _views[0];

    private OutputView? ViewAtCursor() => ViewAt(_cursorX, _cursorY);

    private Workspace? CurrentWorkspace() => ViewAtCursor()?.Active;

    private void InitWorkspaces(OutputView view)
    {
        view.GroupId = ++_workspaceIds;
        var workspace = NewWorkspace(view, null, view.Workspaces.Count);
        workspace.Tree.Enabled = true;
        view.Active = workspace;
        RenumberWorkspaces(view);
        _workspaceModel.RaiseChanged();
    }

    private Workspace NewWorkspace(OutputView view, string? name, int index)
    {
        var id = ++_workspaceIds;
        var workspace = new Workspace
        {
            Id = id,
            Name = string.IsNullOrEmpty(name) ? $"{view.Workspaces.Count + 1}" : name,
            Handle = $"ws-{id}",
            Tree = new SceneTree(_windowTree),
        };
        workspace.Tree.Enabled = false;
        view.Workspaces.Insert(index, workspace);
        return workspace;
    }

    private Workspace CreateWorkspace(OutputView view, string? name, bool afterActive)
    {
        var index = afterActive && view.Active is { } active
            ? view.Workspaces.IndexOf(active) + 1
            : view.Workspaces.Count;
        var workspace = NewWorkspace(view, name, index);
        RenumberWorkspaces(view);
        _workspaceModel.RaiseChanged();
        Console.WriteLine($"WORKSPACE + {view.Output.Name} {workspace.Name}");
        return workspace;
    }

    private void RemoveWorkspace(OutputView view, Workspace workspace)
    {
        if (view.Workspaces.Count <= 1)
        {
            return;
        }

        if (workspace == _swipeFrom || workspace == _swipeTo)
        {
            AbortWorkspaceSwipe();
        }

        var index = view.Workspaces.IndexOf(workspace);
        var neighbor = index > 0 ? view.Workspaces[index - 1] : view.Workspaces[1];
        foreach (var window in _windows)
        {
            if (window.Workspace == workspace)
            {
                MoveWindowToWorkspace(window, neighbor);
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (xwindow.Workspace == workspace)
            {
                MoveWindowToWorkspace(xwindow, neighbor);
            }
        }

        if (view.Active == workspace)
        {
            ActivateWorkspace(view, neighbor, index > 0 ? -1 : 1);
        }

        if (_splitWorkspace == workspace)
        {
            DropSplitTransaction();
        }

        view.Workspaces.Remove(workspace);
        workspace.Tree.Destroy();
        RenumberWorkspaces(view);
        _workspaceModel.RaiseChanged();
        Console.WriteLine($"WORKSPACE - {view.Output.Name} {workspace.Name}");
    }

    private void RenumberWorkspaces(OutputView view)
    {
        for (var i = 0; i < view.Workspaces.Count; i++)
        {
            var workspace = view.Workspaces[i];
            if (workspace.Coordinates.Length != 1 || workspace.Coordinates[0] != (uint)i)
            {
                workspace.Coordinates = [(uint)i];
            }
        }
    }

    private int WorkspaceWindowCount(Workspace workspace)
    {
        var count = 0;
        foreach (var window in _windows)
        {
            if (window.Workspace == workspace)
            {
                count++;
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (xwindow.Workspace == workspace)
            {
                count++;
            }
        }

        return count;
    }

    private void ActivateWorkspace(OutputView view, Workspace target, int direction = 0, bool refocus = true, bool sliding = false)
    {
        var current = view.Active;
        if (current == target || !view.Workspaces.Contains(target))
        {
            return;
        }

        target.Tree.Enabled = true;
        target.Urgent = false;
        view.Active = target;
        if (current is not null && !sliding)
        {
            if (_effects.SlideEnabled && view.SceneOutput is not null && !current.Tree.IsDestroyed)
            {
                FinishPendingSlide(except: target);
                if (direction == 0)
                {
                    direction = view.Workspaces.IndexOf(target) > view.Workspaces.IndexOf(current) ? 1 : -1;
                }

                var box = _layout.BoxOf(view.Output);
                current.Tree.ClipBox = box;
                target.Tree.ClipBox = box;
                _slidingFrom = current;
                _slidingTo = target;
                _slidingView = view;
                _effects.SlideWorkspaces(current.Tree, target.Tree, box, direction, () => FinishPendingSlide());
                view.Scheduler?.ScheduleRepaint();
            }
            else
            {
                current.Tree.Enabled = false;
            }
        }

        _workspaceModel.RaiseChanged();
        Console.WriteLine($"WORKSPACE {view.Output.Name} {target.Name}");
        if (refocus)
        {
            FocusWorkspaceWindow(target);
        }
    }

    private bool BeginWorkspaceSwipe(uint fingers, uint timeMs) =>
        BeginWorkspaceSwipeAt(ViewAtCursor(), SwipeTravel, SwipeFlingPerSecond, fingers, timeMs);

    private bool BeginWorkspaceSwipeAt(OutputView? at, double travel, double fling, uint fingers, uint timeMs)
    {
        if (_mode != DragMode.None || _effects.SwitcherActive)
        {
            return false;
        }

        if (at is not { } view || view.Active is not { } active || view.Workspaces.Count < 2)
        {
            return false;
        }

        if (!_swipe.Begin(fingers, travel, timeMs))
        {
            return false;
        }

        _swipe.FlingPerSecond = fling;
        var index = view.Workspaces.IndexOf(active);
        _swipe.ClampHigh = index == 0;
        _swipe.ClampLow = index == view.Workspaces.Count - 1;
        _swipeView = view;
        _swipeFrom = active;
        _swipeTo = null;
        _swipeDirection = 0;
        _swipeArmed = false;
        _swipeVisual = false;
        return true;
    }

    private bool UpdateWorkspaceSwipe(double dx, double dy, uint timeMs)
    {
        if (!_swipe.Update(dx, dy, timeMs))
        {
            return false;
        }

        if (_swipeView is not { } view || _swipeFrom is not { } from)
        {
            return true;
        }

        if (!_swipeArmed && _swipe.Direction != 0)
        {
            ArmWorkspaceSwipe(view, from, _swipe.Direction);
        }

        if (_swipeVisual)
        {
            _effects.SlideProgress = Math.Max(0, _swipe.Progress * _swipeDirection);
            view.Scheduler?.ScheduleRepaint();
        }

        return true;
    }

    private void ArmWorkspaceSwipe(OutputView view, Workspace from, int direction)
    {
        _swipeArmed = true;
        _swipeDirection = direction;
        var index = view.Workspaces.IndexOf(from);
        var target = direction > 0 ? index - 1 : index + 1;
        _swipeTo = target >= 0 && target < view.Workspaces.Count ? view.Workspaces[target] : null;

        if (!_effects.SlideEnabled || view.SceneOutput is null || from.Tree.IsDestroyed)
        {
            return;
        }

        FinishPendingSlide(except: _swipeTo);
        var box = _layout.BoxOf(view.Output);
        from.Tree.ClipBox = box;
        if (_swipeTo is { } incoming && !incoming.Tree.IsDestroyed)
        {
            incoming.Tree.Enabled = true;
            incoming.Tree.ClipBox = box;
        }

        _effects.DragWorkspaces(from.Tree, _swipeTo?.Tree, box, direction > 0 ? -1 : 1);
        _swipeVisual = true;
    }

    private bool EndWorkspaceSwipe(bool cancelled, uint timeMs)
    {
        var outcome = _swipe.End(cancelled, timeMs);
        if (outcome == SwipeOutcome.None)
        {
            return false;
        }

        var view = _swipeView;
        var from = _swipeFrom;
        var to = _swipeTo;
        var visual = _swipeVisual;
        _swipeView = null;
        _swipeFrom = null;
        _swipeTo = null;
        _swipeDirection = 0;
        _swipeArmed = false;
        _swipeVisual = false;

        var commit = outcome == SwipeOutcome.Commit
            && view is not null && from is not null && to is not null
            && !from.Tree.IsDestroyed && !to.Tree.IsDestroyed
            && view.Workspaces.Contains(to);

        if (visual)
        {
            _slidingView = view;
            if (commit)
            {
                _slidingFrom = from;
                _slidingTo = to;
            }
            else
            {
                _slidingFrom = to ?? from;
                _slidingTo = to is null ? null : from;
            }

            _effects.SettleSlide(commit, () => FinishPendingSlide());
            view?.Scheduler?.ScheduleRepaint();
        }

        if (commit)
        {
            ActivateWorkspace(view!, to!, refocus: true, sliding: visual);
        }

        return true;
    }

    private void AbortWorkspaceSwipe()
    {
        _touchSwipe = TouchSwipeState.Idle;
        _touchSwipeTravel = 0;
        if (!_swipe.IsActive)
        {
            return;
        }

        _ = EndWorkspaceSwipe(cancelled: true, timeMs: 0);
    }

    private bool TouchSwipeDown(int id, double x, double y)
    {
        _contacts.Down(id, x, y);
        if (_touchSwipe is TouchSwipeState.Claimed or TouchSwipeState.Spent)
        {
            return true;
        }

        if (_contacts.Count == (int)TouchSwipeFingers && _mode == DragMode.None &&
            !_effects.SwitcherActive && _touchFramePress is null && _touchDragSlot is null)
        {
            _touchSwipe = TouchSwipeState.Watching;
            _touchSwipeTravel = 0;
        }

        return false;
    }

    private bool TouchSwipeMotion(int id, double x, double y, uint timeMs)
    {
        if (!_contacts.Motion(id, x, y, out var dx, out var dy))
        {
            return _touchSwipe is TouchSwipeState.Claimed or TouchSwipeState.Spent;
        }

        switch (_touchSwipe)
        {
            case TouchSwipeState.Watching:
                _touchSwipeTravel += dx;
                return Math.Abs(_touchSwipeTravel) >= TouchSwipeSlop && ClaimTouchSwipe(timeMs);

            case TouchSwipeState.Claimed:
                _ = UpdateWorkspaceSwipe(dx, dy, timeMs);
                return true;

            case TouchSwipeState.Spent:
                return true;

            default:
                return false;
        }
    }

    private bool ClaimTouchSwipe(uint timeMs)
    {
        var view = _contacts.TryCentroid(out var centerX, out var centerY) ? ViewAt(centerX, centerY) : null;
        var travel = view is null ? 0 : _layout.BoxOf(view.Output).Width;
        if (travel <= 0 ||
            !BeginWorkspaceSwipeAt(view, travel, travel * TouchFlingFraction, TouchSwipeFingers, timeMs))
        {
            _touchSwipe = TouchSwipeState.Idle;
            return false;
        }

        _touchSwipe = TouchSwipeState.Claimed;
        if (_touchPointer.Cancel())
        {
            OnButton(timeMs, BtnLeft, pressed: false);
        }

        _touchPoints.Clear();
        _seat.Touch.NotifyCancel();
        return true;
    }

    private bool TouchSwipeUp(int id, uint timeMs)
    {
        _ = _contacts.Up(id);
        switch (_touchSwipe)
        {
            case TouchSwipeState.Watching when _contacts.Count < (int)TouchSwipeFingers:
                _touchSwipe = TouchSwipeState.Idle;
                return false;

            case TouchSwipeState.Claimed:
                _ = EndWorkspaceSwipe(cancelled: false, timeMs);
                _touchSwipe = _contacts.Count > 0 ? TouchSwipeState.Spent : TouchSwipeState.Idle;
                return true;

            case TouchSwipeState.Spent:
                if (_contacts.Count == 0)
                {
                    _touchSwipe = TouchSwipeState.Idle;
                }

                return true;

            default:
                return false;
        }
    }

    private void TouchSwipeCancel()
    {
        _contacts.Clear();
        AbortWorkspaceSwipe();
    }

    private void FinishPendingSlide(Workspace? except = null)
    {
        if (_slidingFrom is not { } from)
        {
            return;
        }

        var to = _slidingTo;
        var view = _slidingView;
        _slidingFrom = null;
        _slidingTo = null;
        _slidingView = null;
        if (!from.Tree.IsDestroyed)
        {
            from.Tree.ClipBox = default;
            if (from != except && view is not null && view.Active != from)
            {
                from.Tree.Enabled = false;
            }
        }

        if (to is not null && !to.Tree.IsDestroyed)
        {
            to.Tree.ClipBox = default;
        }
    }

    private void FocusWorkspaceWindow(Workspace workspace)
    {
        switch (workspace.LastFocused)
        {
            case Window window when _windows.Contains(window) && window.Workspace == workspace:
                FocusWindow(window);
                return;

            case XWindow xwindow when _xwindows.Contains(xwindow) && xwindow.Workspace == workspace:
                FocusXWindow(xwindow);
                return;
        }

        for (var i = _windows.Count - 1; i >= 0; i--)
        {
            if (_windows[i].Workspace == workspace)
            {
                FocusWindow(_windows[i]);
                return;
            }
        }

        for (var i = _xwindows.Count - 1; i >= 0; i--)
        {
            if (_xwindows[i].Workspace == workspace)
            {
                FocusXWindow(_xwindows[i]);
                return;
            }
        }

        FocusWindow(null);
        _focusedX?.SetDecorationFocus(false);
        _focusedX = null;
    }

    private void SwitchWorkspace(int delta)
    {
        if (ViewAtCursor() is not { } view || view.Active is not { } active || view.Workspaces.Count < 2)
        {
            return;
        }

        var index = view.Workspaces.IndexOf(active);
        var count = view.Workspaces.Count;
        var next = ((index + delta) % count + count) % count;
        ActivateWorkspace(view, view.Workspaces[next], Math.Sign(delta));
    }

    private void CarryFocusedWindow(int delta)
    {
        if (ViewAtCursor() is not { } view || view.Active is not { } active)
        {
            return;
        }

        IGrabTarget? focused = _focused is not null ? _focused : _focusedX;
        if (focused is null)
        {
            return;
        }

        Workspace target;
        if (view.Workspaces.Count < 2)
        {
            var at = view.Workspaces.IndexOf(active) + (delta > 0 ? 1 : 0);
            target = NewWorkspace(view, null, at);
            RenumberWorkspaces(view);
            _workspaceModel.RaiseChanged();
            Console.WriteLine($"WORKSPACE + {view.Output.Name} {target.Name}");
        }
        else
        {
            var index = view.Workspaces.IndexOf(active);
            var count = view.Workspaces.Count;
            target = view.Workspaces[((index + delta) % count + count) % count];
        }

        MoveWindowToWorkspace(focused, target, refocus: false);
        target.LastFocused = focused;
        ActivateWorkspace(view, target, Math.Sign(delta));
    }

    private void ReassignDraggedWorkspace(IGrabTarget window)
    {
        var workspace = window switch
        {
            Window w => w.Workspace,
            XWindow x => x.Workspace,
            _ => null,
        };
        if (workspace is null)
        {
            return;
        }

        var (width, height) = window.GeometrySize;
        var under = _layout.OutputAt(window.X + (width / 2), window.Y + (height / 2));
        var view = _views.FirstOrDefault(v => v.Output == under);
        if (view is null || view.Active is not { } target || ViewOf(workspace) == view)
        {
            return;
        }

        MoveWindowToWorkspace(window, target, translate: false);
        target.LastFocused = window;
    }

    private void MoveWindowToWorkspace(IGrabTarget window, Workspace target, bool refocus = true, bool translate = true)
    {
        Workspace? source = null;
        string? moved = null;
        switch (window)
        {
            case Window w when w.Workspace != target:
                source = w.Workspace;
                if (source is not null && source.Tiled.Contains(w))
                {
                    DissolveSplit(source);
                }

                w.Workspace = target;
                w.Tree?.Reparent(target.Tree);
                SaveSession(w.Toplevel);
                moved = w.Toplevel.AppId;
                break;

            case XWindow x when x.Workspace != target:
                source = x.Workspace;
                x.Workspace = target;
                x.Tree.Reparent(target.Tree);
                moved = x.XWin.Class;
                break;
        }

        if (moved is not null)
        {
            var targetView = ViewOf(target);
            if (translate && source is not null && ViewOf(source) is { } from && targetView is { } to && from != to)
            {
                var sourceBox = _layout.BoxOf(from.Output);
                var targetBox = _layout.BoxOf(to.Output);
                window.MoveTo(window.X - sourceBox.X + targetBox.X, window.Y - sourceBox.Y + targetBox.Y);
            }

            Console.WriteLine($"WORKSPACE {moved} > {target.Name}");
            if (refocus && targetView is { } hidden && hidden.Active != target &&
                (ReferenceEquals(_focused, window) || ReferenceEquals(_focusedX, window)))
            {
                if (source is not null)
                {
                    FocusWorkspaceWindow(source);
                }
                else
                {
                    FocusWindow(null);
                }
            }
        }

        _workspaceModel.RaiseMembersChanged();
    }

    private void DissolveSplit(Workspace workspace)
    {
        if (_splitWorkspace == workspace)
        {
            DropSplitTransaction();
        }

        workspace.Tiled.Clear();
    }

    private void AssignWorkspace(OutputView source, Workspace workspace, OutputView target)
    {
        source.Workspaces.Remove(workspace);
        if (source.Active == workspace)
        {
            var fallback = source.Workspaces[0];
            fallback.Tree.Enabled = true;
            source.Active = fallback;
        }

        target.Workspaces.Add(workspace);
        workspace.Tree.Enabled = target.Active == workspace;

        var sourceBox = _layout.BoxOf(source.Output);
        var targetBox = _layout.BoxOf(target.Output);
        foreach (var window in _windows)
        {
            if (window.Workspace == workspace)
            {
                window.MoveTo(window.X - sourceBox.X + targetBox.X, window.Y - sourceBox.Y + targetBox.Y);
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (xwindow.Workspace == workspace)
            {
                xwindow.MoveTo(xwindow.X - sourceBox.X + targetBox.X, xwindow.Y - sourceBox.Y + targetBox.Y);
            }
        }

        RenumberWorkspaces(source);
        RenumberWorkspaces(target);
        _workspaceModel.RaiseChanged();
        Console.WriteLine($"WORKSPACE {workspace.Name} > {target.Output.Name}");
    }

    private void DropWorkspacesOf(OutputView view)
    {
        var fallback = _views.FirstOrDefault(v => v != view);
        foreach (var workspace in view.Workspaces.ToArray())
        {
            foreach (var window in _windows)
            {
                if (window.Workspace == workspace)
                {
                    window.Workspace = fallback?.Active;
                    window.Tree?.Reparent(fallback?.Active?.Tree ?? _windowTree);
                }
            }

            foreach (var xwindow in _xwindows)
            {
                if (xwindow.Workspace == workspace)
                {
                    xwindow.Workspace = fallback?.Active;
                    xwindow.Tree.Reparent(fallback?.Active?.Tree ?? _windowTree);
                }
            }

            if (_splitWorkspace == workspace)
            {
                DropSplitTransaction();
            }

            workspace.Tree.Destroy();
        }

        view.Workspaces.Clear();
        view.Active = null;
        _workspaceModel.RaiseChanged();
    }

    private void MarkUrgent(Workspace workspace)
    {
        if (workspace.Urgent)
        {
            return;
        }

        workspace.Urgent = true;
        _workspaceModel.RaiseChanged();
        Console.WriteLine($"URGENT {workspace.Name}");
    }

    private void PrintWorkspaces()
    {
        for (var i = 0; i < _views.Count; i++)
        {
            var view = _views[i];
            var cells = view.Workspaces.Select(ws =>
                $"[{ws.Name}{(view.Active == ws ? "*" : "")}{(ws.Urgent ? "!" : "")}:{WorkspaceWindowCount(ws)}]");
            Console.WriteLine($"WS output={i} {string.Join(" ", cells)}");
        }
    }
}
