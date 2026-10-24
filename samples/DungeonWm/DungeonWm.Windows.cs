using System.Numerics;
using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Scene3D;
using Basin.Scene3D.Scene;
using Basin.Shell.Xdg;

namespace DungeonWm;

internal sealed partial class DungeonWm
{
    private const float SlotMargin = 0.2f;

    private void OnNewToplevel(XdgToplevelWindow toplevel)
    {
        var scene = new SceneSurface(_layers.Windows, toplevel.Surface);
        scene.Tree.Enabled = false;
        _popups.Add(toplevel.Xdg, scene);
        var window = new DungeonWmWindow(toplevel, scene);

        toplevel.Xdg.Mapped += () => OnMapped(window);
        toplevel.Xdg.Unmapped += () => OnUnmapped(window);
        toplevel.Xdg.Committed += () => OnCommitted(window);
        toplevel.Destroyed += () =>
        {
            OnUnmapped(window);
            _popups.Remove(toplevel.Xdg);
        };
        toplevel.FullscreenRequested += fullscreen =>
        {
            if (fullscreen)
            {
                EnterTakeover(window);
            }
            else if (ReferenceEquals(_takeover, window))
            {
                LeaveTakeover();
            }
            else
            {
                toplevel.RequestConfigure();
            }
        };
        toplevel.MaximizeRequested += maximized =>
        {
            if (maximized)
            {
                EnterTakeover(window);
            }
            else if (ReferenceEquals(_takeover, window))
            {
                LeaveTakeover();
            }
            else
            {
                toplevel.RequestConfigure();
            }
        };
        toplevel.MinimizeRequested += () => toplevel.RequestConfigure();
    }

    private void OnNewPopup(XdgPopupWindow popup) => _popups.Attach(popup);

    private void OnMapped(DungeonWmWindow window)
    {
        if (_windows.Contains(window))
        {
            return;
        }

        window.Scene.Tree.Enabled = true;
        window.ToplevelId = ToplevelIdOf(window.Surface);
        _windows.Add(window);
        SyncSize(window);
        var room = _world.RoomAt(_camera.Position) ?? _workspaces.Active ?? _world.Map.Rooms[0];
        Hang(window, room, null);
        _world.World.Windows.Add(window.World);
        _workspaces.RaiseMembersChanged();
        BasinReport.Line($"MAPPED {window.AppId} room={room.Workspace + 1} slot={SlotName(window.Slot)} size={window.World.SurfaceWidth}x{window.World.SurfaceHeight}");
        StartHang(window);
        AudioWindowMapped(window);
        ScheduleAll();
    }

    private void OnUnmapped(DungeonWmWindow window)
    {
        if (!_windows.Remove(window))
        {
            return;
        }

        if (ReferenceEquals(_takeover, window))
        {
            LeaveTakeover();
        }

        var snapshot = TakeSnapshot(window);
        if (snapshot is not null)
        {
            AdoptSnapshot(window, snapshot);
        }

        AudioWindowUnmapped(window, snapshot);

        window.Scene.Tree.Enabled = false;
        window.Hanging = -1;
        UnbindBody(window);
        _world.Release(window);
        _world.World.Windows.Remove(window.World);
        _worldRenderer.Forget(window.World);
        _plaques.Forget(window);
        window.Fade = window.CanvasFade = 1;
        if (ReferenceEquals(_plaqueHover, window))
        {
            _plaqueHover = null;
        }

        if (ReferenceEquals(_plaquePress, window))
        {
            _plaquePress = null;
        }

        if (ReferenceEquals(_focused, window))
        {
            _focused = null;
            if (!LayerHoldsKeyboard)
            {
                _seat.Keyboard.NotifyClearFocus();
            }

            SyncMode();
        }

        _workspaces.RaiseMembersChanged();
        BasinReport.Line($"UNMAPPED {window.AppId}");
        ScheduleAll();
    }

    private void OnCommitted(DungeonWmWindow window)
    {
        if (!_windows.Contains(window))
        {
            return;
        }

        var before = (window.World.SurfaceWidth, window.World.SurfaceHeight);
        SyncSize(window);
        if (before != (window.World.SurfaceWidth, window.World.SurfaceHeight) && window.State == DungeonWmWindowState.Hung && window.Room is { } room)
        {
            var slot = window.Slot;
            _world.Release(window);
            Hang(window, room, slot);
        }

        ScheduleAll();
    }

    private void SyncSize(DungeonWmWindow window)
    {
        var geometry = window.Geometry;
        var width = Math.Max(1, geometry.Width);
        var height = Math.Max(1, geometry.Height);
        window.World.SurfaceWidth = width;
        window.World.SurfaceHeight = height;
        if (window.State == DungeonWmWindowState.Takeover)
        {
            if (_takeoverFit is { } fit && ReferenceEquals(window, _takeover))
            {
                window.Scene.Tree.SetPosition(-geometry.X, -geometry.Y);
                fit.Update(fit.Target, (width, height), _settings.TakeoverScaler);
            }

            return;
        }

        var scale = FitScale(width, height, window.Wide);
        window.World.Width = width / _settings.PixelsPerMeter * scale;
        window.World.Height = height / _settings.PixelsPerMeter * scale;
        window.World.Depth = _settings.WindowDepth;
        window.World.EdgeColor = _settings.EdgeColor;
        window.World.FocusColor = _settings.FocusColor;
        _frames.Apply(window.World, window.FrameStyle, shown: true);
    }

    private float FitScale(int width, int height, bool wide)
    {
        var unit = _world.Unit;
        var maxWidth = ((wide ? 8 : 4) * unit) - SlotMargin - _frames.ExtraWidth;
        var maxHeight = (RoomBaker.WallHeight * unit) - 0.5f - _frames.ExtraHeight;
        var meters = new Vector2(width, height) / _settings.PixelsPerMeter;
        return MathF.Max(0.05f, MathF.Min(1, MathF.Min(maxWidth / meters.X, maxHeight / meters.Y)));
    }

    private void Hang(DungeonWmWindow window, MapRegion room, WallSlot? preferred)
    {
        _world.Release(window);
        var unit = _world.Unit;
        var widthMeters = (window.World.SurfaceWidth / _settings.PixelsPerMeter) + _frames.ExtraWidth;
        var wide = widthMeters > (4 * unit) - SlotMargin;
        window.Wide = wide;
        window.FrameStyle = _frames.StyleOf(room);

        var slot = preferred is { } wanted && _world.IsFree(wanted) && (!wide || (_world.Neighbour(wanted) is { } next && _world.IsFree(next)))
            ? wanted
            : _world.FreeSlotFacing(room, _camera.Position, _camera.Flat, wide);
        if (slot is null && wide)
        {
            window.Wide = wide = false;
            slot = _world.FreeSlotFacing(room, _camera.Position, _camera.Flat, false);
        }

        if (slot is null && _world.Map.Grow(room))
        {
            _world.Rebuild();
            ReplaceHungWindows();
            BasinReport.Line($"ROOM grew {room.Workspace + 1} {room.Width}x{room.Depth}");
            slot = _world.FreeSlotFacing(room, _camera.Position, _camera.Flat, wide);
        }

        slot ??= _world.SlotsOf(room).FirstOrDefault();
        if (slot is not { } chosen)
        {
            return;
        }

        _world.Occupy(chosen, window);
        if (wide && _world.Neighbour(chosen) is { } partner)
        {
            _world.Occupy(partner, window);
        }

        window.Slot = chosen;
        window.Room = room;
        window.State = DungeonWmWindowState.Hung;
        window.World.Room = _world.RoomOf(room);

        SyncSize(window);
        PlaceOnSlot(window);
        _frames.Trim(_windows);
        _workspaces.RaiseMembersChanged();
    }

    private void PlaceOnSlot(DungeonWmWindow window)
    {
        if (window.Slot is not { } slot)
        {
            return;
        }

        window.World.Position = HangPosition(window, slot, window.Wide);
        window.World.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, slot.Yaw);
    }

    private Vector3 HangPosition(DungeonWmWindow window, WallSlot slot, bool wide)
    {
        var unit = _world.Unit;
        var center = slot.Center(unit);
        if (wide && _world.Neighbour(slot) is { } partner)
        {
            center = (center + partner.Center(unit)) * 0.5f;
        }

        var frame = window.World.ShownFrame;
        var offWall = frame?.Depth ?? 0.04f;
        var height = frame?.PaintingHeight(window.World.Height) ?? window.World.Height;
        var shift = frame?.PaintingCenterOffset ?? 0;
        var position = center + (slot.Inward * ((0.5f * unit) + offWall));
        var half = height * 0.5f;
        var middle = Math.Clamp(_settings.HangHeight, half + 0.2f, MathF.Max(half + 0.2f, (RoomBaker.WallHeight * unit) - half - 0.15f));
        position.Y = middle - shift;
        return position;
    }

    private void ReplaceHungWindows()
    {
        foreach (var window in _windows)
        {
            if (window.State != DungeonWmWindowState.Hung || window.Room is not { } room || window.Slot is not { } slot)
            {
                continue;
            }

            if (_world.FindSlot(room, (slot.Side, slot.Along)) is { } moved)
            {
                _world.Release(window);
                _world.Occupy(moved, window);
                if (window.Wide && _world.Neighbour(moved) is { } partner)
                {
                    _world.Occupy(partner, window);
                }

                window.Slot = moved;
                window.World.Room = _world.RoomOf(room);
                PlaceOnSlot(window);
            }
        }
    }

    private void SyncLayers(DungeonWmWindow window)
    {
        var world = window.World;
        world.ClearLayers();
        var geometry = window.Geometry;
        var (originX, originY) = window.Scene.Tree.OffsetFromRoot;
        originX += geometry.X;
        originY += geometry.Y;
        var level = 0;
        WorldSceneLayers.Collect(world, window.Scene.Tree, _renderer, originX, originY, ref level);
        if (window.X is not null)
        {
            foreach (var (overrideRedirect, scene) in _overrides)
            {
                if (ReferenceEquals(OwnerOf(overrideRedirect), window) && scene.Tree.Enabled)
                {
                    level++;
                    WorldSceneLayers.Collect(world, scene.Tree, _renderer, originX, originY, ref level);
                }
            }
        }

        world.Focused = ReferenceEquals(_focused, window);
        world.Opacity = MathF.Min(window.Fade, world.Focused ? 1 : _settings.UnfocusedOpacity);
        world.CanvasOpacity = window.CanvasFade;
    }

    private void Focus(DungeonWmWindow? window)
    {
        if (ReferenceEquals(_focused, window))
        {
            return;
        }

        _focused?.SetActivated(false);
        _focused = window;
        if (window is null)
        {
            if (!LayerHoldsKeyboard)
            {
                _seat.Keyboard.NotifyClearFocus();
            }
        }
        else
        {
            window.SetActivated(true);
            if (!LayerKeepsKeyboard)
            {
                _layerKeyboard = null;
                _seat.Keyboard.NotifyEnter(window.Surface);
            }

            BasinReport.Line($"FOCUS {window.AppId}");
        }

        SyncMode();
        ScheduleAll();
    }

    private DungeonWmWindow? WindowOf(Surface surface)
    {
        for (Surface? candidate = surface; candidate is not null; candidate = candidate.SubsurfaceRole?.Parent)
        {
            foreach (var window in _windows)
            {
                if (ReferenceEquals(window.Surface, candidate))
                {
                    return window;
                }
            }
        }

        foreach (var window in _windows)
        {
            var layers = window.World.Layers;
            for (var i = 0; i < layers.Length; i++)
            {
                if (ReferenceEquals(layers[i].Surface, surface))
                {
                    return window;
                }
            }
        }

        return null;
    }

    private ulong ToplevelIdOf(Surface surface) =>
        _toplevels.TryFind(surface, out var info) ? info.Id : 0;

    private SceneFit? _takeoverFit;

    private SceneTree? _takeoverHome;

    private void EnterTakeover(DungeonWmWindow window)
    {
        if (_outputs.Views.Count == 0 || ReferenceEquals(_takeover, window))
        {
            return;
        }

        if (_takeover is null && _pendingTakeover is null)
        {
            _crosshairBeforeTakeover = _focused is null;
        }

        if (_takeover is { } previous)
        {
            LeaveTakeover();
        }

        if (window.State == DungeonWmWindowState.Hung && !ReferenceEquals(_pendingTakeover, window))
        {
            _beforeTakeover = (_camera.Position, _camera.Yaw, _camera.Pitch);
            _pendingTakeover = window;
            FaceWindow(window, fly: true);
            BasinReport.Line($"TAKEOVER flying {window.AppId}");
            return;
        }

        _pendingTakeover = null;

        var view = OutputUnderPointer() ?? _outputs.Views[0];
        var box = _layout.BoxOf(view.Output);
        _takeover = window;
        window.State = DungeonWmWindowState.Takeover;
        if (window.World.Frame is { } frame)
        {
            frame.Visible = false;
        }

        foreach (var other in _windows)
        {
            other.Scene.Tree.Enabled = ReferenceEquals(other, window);
        }

        var geometry = window.Geometry;
        if (_settings.TakeoverScaler is not null && window.Scene.Tree.Parent is { } home)
        {
            _takeoverHome = home;
            _takeoverFit = new SceneFit(home) { Filter = _settings.TakeoverFilter };
            window.Scene.Tree.Reparent(_takeoverFit.Content);
            window.Scene.Tree.SetPosition(-geometry.X, -geometry.Y);
            _takeoverFit.Update(box, (Math.Max(1, geometry.Width), Math.Max(1, geometry.Height)), _settings.TakeoverScaler);
            _takeoverFit.Root.RaiseToTop();
        }
        else
        {
            window.Scene.Tree.SetPosition(box.X - geometry.X, box.Y - geometry.Y);
            window.Scene.Tree.RaiseToTop();
        }

        if (_settings.TakeoverFrameLimit > 0 && window.Scene.Surface is { } limited)
        {
            _services.Find<Basin.Capabilities.IFrameLimits>()?.SetLimit(limited, _settings.TakeoverFrameLimit);
        }

        window.SetFullscreen(true, box);
        ApplyLayerState();
        Focus(window);
        BasinReport.Line($"TAKEOVER {window.AppId} on {view.Output.Name}");
        RaiseEvent("dungeonwm/takeover");
        ScheduleAll();
    }

    private void LeaveTakeover()
    {
        if (_takeover is not { } window)
        {
            return;
        }

        _takeover = null;
        foreach (var other in _windows)
        {
            other.Scene.Tree.Enabled = true;
        }

        if (_takeoverFit is { } fit)
        {
            if (_takeoverHome is { IsDestroyed: false } home && !window.Scene.Tree.IsDestroyed)
            {
                window.Scene.Tree.Reparent(home);
            }

            fit.Destroy();
            _takeoverFit = null;
            _takeoverHome = null;
        }

        if (_settings.TakeoverFrameLimit > 0 && window.Scene.Surface is { } limited)
        {
            _services.Find<Basin.Capabilities.IFrameLimits>()?.SetLimit(limited, 0);
        }

        if (window.Toplevel is not null)
        {
            window.Scene.Tree.SetPosition(0, 0);
        }

        window.SetFullscreen(false, default);
        window.State = DungeonWmWindowState.Hung;
        if (window.Room is { } room)
        {
            var slot = window.Slot;
            Hang(window, room, slot);
        }

        ApplyLayerState();
        if (_beforeTakeover is { } pose)
        {
            _walker.FlyTo(pose.Position, pose.Yaw, pose.Pitch);
            _beforeTakeover = null;
        }

        if (_crosshairBeforeTakeover)
        {
            Focus(null);
        }

        SyncMode();

        BasinReport.Line($"TAKEOVER off {window.AppId}");
        RaiseEvent("dungeonwm/takeover");
        ScheduleAll();
    }

    private void CompletePendingTakeover()
    {
        if (_pendingTakeover is { } pending && !_walker.Flying)
        {
            if (_windows.Contains(pending))
            {
                pending.State = DungeonWmWindowState.Pending;
                EnterTakeover(pending);
            }
            else
            {
                _pendingTakeover = null;
            }
        }
    }

    private static string SlotName(WallSlot? slot) =>
        slot is { } s ? $"{s.Side.ToString().ToLowerInvariant()}:{s.Along}" : "none";
}
