using Basin.Scene;

namespace EightWm;

internal sealed partial class Shell
{
    private readonly List<AppWindow> _parked = [];
    private SceneTree? _parking;
    private Basin.Box _lastAppArea = new(0, 0, 1280, 720);

    internal int ParkedCount => _parked.Count;

    private void Evacuate(ShellView gone)
    {
        _lastAppArea = AppArea(gone);
        if (DraggedTitle is not null)
        {
            TitleCancel();
        }

        if (DraggedEntry is not null)
        {
            RailCancel();
        }

        if (ReferenceEquals(_kbView, gone))
        {
            _kbView = null;
            _kbShown = false;
        }

        if (_panApp is { } panned && ReferenceEquals(HomeOf(panned), gone))
        {
            _panApp = null;
            _panRunning = false;
            _panNow = 0;
            _panTo = 0;
        }

        var leaving = new List<AppWindow>(gone.Host.Mru);
        foreach (var (app, view) in _homes)
        {
            if (ReferenceEquals(view, gone) && !leaving.Contains(app))
            {
                leaving.Add(app);
            }
        }

        var newest = gone.Host.Active;
        foreach (var app in leaving)
        {
            gone.Host.Forget(app);
            _homes.Remove(app);
            SetPan(app, 0);
        }

        if (Views.Count == 0)
        {
            foreach (var app in leaving)
            {
                Park(app);
            }
        }
        else
        {
            Settle(leaving, newest, PlacementView());
        }

        if (_ownLocked)
        {
            if (Views.Count > 0)
            {
                BuildLockScreens();
                ApplyLock();
            }
            else
            {
                DisposeLockScreens();
                _lockView = null;
            }
        }

        _report.Line($"EVACUATE {gone.Output.Name} apps={leaving.Count} parked={_parked.Count}");
    }

    private void Park(AppWindow app)
    {
        _parking ??= new SceneTree(_scene.Root) { Enabled = false };
        if (!app.Slot.IsDestroyed)
        {
            app.Slot.Reparent(_parking);
        }

        _parked.Remove(app);
        _parked.Add(app);
    }

    private void Unpark(ShellView view)
    {
        if (_ownLocked && (_lockView is null || !_lockScreens.ContainsKey(_lockView)))
        {
            BuildLockScreens();
            ApplyLock();
        }

        if (_parked.Count == 0)
        {
            return;
        }

        var returning = new List<AppWindow>(_parked);
        _parked.Clear();
        var newest = returning.Find(static app => !app.IsTransient);
        Settle(returning, newest, view);
        _report.Line($"UNPARK {view.Output.Name} apps={returning.Count}");
    }

    private void Settle(List<AppWindow> apps, AppWindow? newest, ShellView target)
    {
        for (var i = apps.Count - 1; i >= 0; i--)
        {
            var app = apps[i];
            _homes[app] = target;
            if (app.Slot.IsDestroyed)
            {
                continue;
            }

            app.Slot.Reparent(app.IsTransient ? target.Transients : target.Apps);
            app.Slot.Enabled = true;
            if (!app.IsTransient)
            {
                target.Host.Touch(app);
                app.Hidden();
            }
        }

        if (newest is not null && target.Host.IsEmpty && _homes.ContainsKey(newest))
        {
            target.Host.Replace(newest);
            target.StartVisible = false;
            Relayout(target);
            if (!_locked)
            {
                Focus(newest);
            }
        }
        else
        {
            Relayout(target);
        }

        _capture.Stack.RaiseChanged();
    }
}
