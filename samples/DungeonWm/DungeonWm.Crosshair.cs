using System.Globalization;
using System.Numerics;
using Basin;
using Basin.Backend.Wayland;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Ipc;
using Basin.Scene;
using Basin.Scene3D;

namespace DungeonWm;

internal sealed partial class DungeonWm
{
    private bool _crosshair;
    private bool _crosshairBeforeTakeover;
    private string _reticle = string.Empty;
    private SceneOutput? _reticleScene;

    private bool WantsCrosshair =>
        _focused is null && _takeover is null && !_locked && !LayerHoldsKeyboard && !InspectorOpen && _outputs.Views.Count > 0;

    private void SyncMode()
    {
        if (_binder is null)
        {
            return;
        }

        var wanted = WantsCrosshair;
        if (wanted != _crosshair)
        {
            _crosshair = wanted;
            if (wanted)
            {
                EnterCrosshair();
            }
            else
            {
                LeaveCrosshair();
            }
        }

        ReportModeChange();
    }

    private string ModeName => InspectorOpen ? "inspector" : _crosshair ? "crosshair" : "window";

    private void EnterCrosshair()
    {
        _looking = false;
        _seat.Pointer.NotifyClearFocus();
        _cursor.SetHover(null, overClient: false);
        Recenter();
        LockParent();
        _reticle = string.Empty;
        UpdateReticle();
    }

    private void LeaveCrosshair()
    {
        UnlockParent();
        _reticle = string.Empty;
        HoverPlaque(WorldHit.Miss);
        PointerMoved((uint)Environment.TickCount);
    }

    private OutputView? CrosshairView()
    {
        if (_settings.CrosshairOutput is { } name)
        {
            foreach (var view in _outputs.Views)
            {
                if (view.Output.Name == name)
                {
                    return view;
                }
            }
        }

        return _outputs.Views.Count > 0 ? _outputs.Views[0] : null;
    }

    private (double X, double Y) LockPoint()
    {
        if (CrosshairView() is not { } view)
        {
            return (_pointer.X, _pointer.Y);
        }

        var box = _layout.BoxOf(view.Output);
        return (box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
    }

    private void Recenter()
    {
        var (x, y) = LockPoint();
        _pointer.Warp(x, y);
        _cursor.MoveTo(x, y);
        MoveReticle();
    }

    private void Relock()
    {
        if (!_crosshair)
        {
            return;
        }

        UnlockParent();
        Recenter();
        LockParent();
        if (_reticle.Length > 0)
        {
            ShowReticle(_reticle);
        }

        UpdateReticle();
    }

    private void LockParent()
    {
        if (_host.Parent is null)
        {
            return;
        }

        _cursor.Hide();
        _reticleScene = CrosshairView()?.Scene;
        if (_binder.ParentLock is { } parentLock && CrosshairView()?.Output is WaylandOutput output)
        {
            parentLock.Request(this, output);
        }
    }

    private void UnlockParent()
    {
        if (_binder.ParentLock is { } parentLock && parentLock.IsRequested(this))
        {
            var (x, y) = LockPoint();
            parentLock.Release(this, x, y);
        }

        if (_reticleScene is { } scene)
        {
            scene.SetSoftwareCursor(null, 0, 0);
            _reticleScene = null;
            _cursor.Reveal();
        }
    }

    private void ForgetCrosshairOutput(OutputView view)
    {
        _binder?.ParentLock?.Forget(view.Output);

        if (ReferenceEquals(_reticleScene, view.Scene))
        {
            _reticleScene = null;
        }
    }

    private void CrosshairLook(double dx, double dy)
    {
        var sensitivity = _settings.LookSensitivity;
        _walker.Look(-(float)dx * sensitivity, -(float)dy * sensitivity);
        FollowCrosshair();
        ScheduleAll();
    }

    private void FollowCrosshair()
    {
        if (!UpdatePull() && _resizing && _resized is { } window)
        {
            ContinueResize(window);
        }

        UpdateReticle();
    }

    private bool Reachable(in WorldHit pick) =>
        pick.Kind != WorldHitKind.None && Vector3.Distance(_camera.Position, pick.Point) <= _settings.Reach;

    private void UpdateReticle()
    {
        if (!_crosshair)
        {
            return;
        }

        var shape = Reticle.Idle;
        var dim = false;
        if (_pull is not null || _resizing)
        {
            shape = Reticle.Dragging;
            HoverPlaque(WorldHit.Miss);
        }
        else
        {
            var pick = PickAt(_pointer.X, _pointer.Y);
            if (pick.Window?.Tag is DungeonWmWindow window)
            {
                shape = pick.Kind switch
                {
                    WorldHitKind.Surface => Reticle.Use,
                    WorldHitKind.Plaque => PlaqueAt(pick) == PlaqueButton.None ? Reticle.Idle : Reticle.Use,
                    WorldHitKind.Frame when IsGrip(pick) && window.State == DungeonWmWindowState.Hung =>
                        Reticle.Resize(Edges(pick.Part).X, Edges(pick.Part).Y),
                    WorldHitKind.Frame => Reticle.Grab,
                    _ => Reticle.Idle,
                };
            }

            var reachable = Reachable(pick);
            dim = shape != Reticle.Idle && !reachable;
            HoverPlaque(reachable ? pick : WorldHit.Miss);
        }

        var name = Reticle.Name(shape, dim);
        if (ReferenceEquals(name, _reticle))
        {
            return;
        }

        _reticle = name;
        ShowReticle(name);
        BasinReport.Line($"RETICLE {name["reticle-".Length..]}");
    }

    private void ShowReticle(string name)
    {
        if (_reticleScene is not { } scene)
        {
            _cursor.ShowNamed(name);
            return;
        }

        var view = CrosshairView();
        if (view is null || _cursor.Images?.Named(name, new CursorKey(view.Output.Scale, null)) is not { } image)
        {
            return;
        }

        scene.SetSoftwareCursor(image.Buffer, image.HotspotX, image.HotspotY);
        MoveReticle();
    }

    private void MoveReticle()
    {
        if (_reticleScene is not { } scene || CrosshairView() is not { } view)
        {
            return;
        }

        var (x, y) = LockPoint();
        var box = _layout.BoxOf(view.Output);
        scene.MoveSoftwareCursor((int)((x - box.X) * view.Output.Scale), (int)((y - box.Y) * view.Output.Scale));
    }

    private void DefineReticles()
    {
        var size = _settings.ReticleSize;
        var color = _settings.ReticleColor;
        for (var shape = 0; shape < Reticle.Shapes.Length; shape++)
        {
            var drawn = shape;
            _cursor.Define(Reticle.Name(drawn, dim: false), scale => Reticle.Paint(drawn, false, scale, size, color));
            _cursor.Define(Reticle.Name(drawn, dim: true), scale => Reticle.Paint(drawn, true, scale, size, color));
        }

        if (_crosshair && _reticle.Length > 0)
        {
            ShowReticle(_reticle);
        }
    }

    private void CrosshairPress(uint button)
    {
        if (button != ButtonLeft)
        {
            return;
        }

        var pick = PickAt(_pointer.X, _pointer.Y);
        if (pick.Window?.Tag is not DungeonWmWindow window || !Reachable(pick))
        {
            return;
        }

        switch (pick.Kind)
        {
            case WorldHitKind.Plaque:
                PressPlaque(window, PlaqueAt(pick));
                break;
            case WorldHitKind.Frame when IsGrip(pick) && window.State == DungeonWmWindowState.Hung:
                BeginResize(window, pick);
                break;
            case WorldHitKind.Frame or WorldHitKind.Surface:
                BeginPull(pick);
                break;
        }

        UpdateReticle();
    }

    private DungeonWmWindow? UseAtCrosshair()
    {
        if (!_crosshair)
        {
            return null;
        }

        var pick = PickAt(_pointer.X, _pointer.Y);
        if (pick.Kind != WorldHitKind.Surface || pick.Window?.Tag is not DungeonWmWindow window || !Reachable(pick))
        {
            return null;
        }

        Use(window);
        return window;
    }

    private void Use(DungeonWmWindow window)
    {
        Focus(window);
        OnInteract(window);
        BasinReport.Line($"USE {window.AppId}");
    }

    private void Release()
    {
        if (_takeover is not null)
        {
            _crosshairBeforeTakeover = true;
            LeaveTakeover();
        }

        Focus(null);
    }

    private void Aim(double x, double y)
    {
        if (!_camera.TryRayAt(_layout, x, y, out var aimed))
        {
            return;
        }

        var wanted = aimed.Direction;
        for (var i = 0; i < 4; i++)
        {
            var (cx, cy) = LockPoint();
            var center = _camera.TryRayAt(_layout, cx, cy, out var locked) ? locked.Direction : wanted;
            var yaw = MathF.Atan2(-wanted.X, -wanted.Z) - MathF.Atan2(-center.X, -center.Z);
            yaw = MathF.IEEERemainder(yaw, 2 * MathF.PI);
            var pitch = MathF.Asin(Math.Clamp(Vector3.Normalize(wanted).Y, -1, 1)) - MathF.Asin(Math.Clamp(Vector3.Normalize(center).Y, -1, 1));
            if (MathF.Abs(yaw) < 1e-5f && MathF.Abs(pitch) < 1e-5f)
            {
                break;
            }

            _walker.Look(yaw, pitch);
        }

        FollowCrosshair();
        ScheduleAll();
    }

    private void RegisterCrosshairCommands(IpcMethodRegistry methods)
    {
        _report.Register(methods, "dungeonwm/use", "use [{id}]",
            Describe("Give a window focus and the free cursor: the one under the crosshair within reach, or the one named by id.", IpcMethodTraits.None),
            (ref IpcParams p, IpcReply _) =>
            {
                DungeonWmWindow? used;
                if (p.TryGetString("id", out var id))
                {
                    used = FindWindow(id);
                    if (used is null)
                    {
                        p.Invalid($"no window {id}");
                        return;
                    }

                    Use(used);
                }
                else
                {
                    used = UseAtCrosshair();
                }

                if (used is null)
                {
                    _report.Line("USE none");
                }
            });

        _report.Register(methods, "dungeonwm/release", "release",
            Describe("Clear the focus and lock the pointer at the crosshair, leaving a takeover first.", IpcMethodTraits.Idempotent),
            Release);

        _report.Register(methods, "dungeonwm/mode", "mode",
            Describe("Print the mode, crosshair or window, with the reticle, the crosshair output and the reach.", IpcMethodTraits.ReadOnly),
            (ref IpcParams _, IpcReply reply) =>
            {
                var shape = _reticle.Length > 0 ? _reticle["reticle-".Length..] : "none";
                var output = CrosshairView()?.Output.Name ?? "none";
                _report.Line(string.Create(CultureInfo.InvariantCulture, $"MODE {ModeName} reticle={shape} output={output} reach={_settings.Reach:F2}"));
                var json = reply.Result;
                json.WriteStartObject();
                json.WriteString("mode"u8, ModeName);
                json.WriteString("reticle"u8, shape);
                json.WriteString("output"u8, output);
                json.WriteNumber("reach"u8, _settings.Reach);
                json.WriteEndObject();
            });

        _report.Register(methods, "dungeonwm/aim", "aim {x:number} {y:number}",
            Describe("Turn the camera so the crosshair points where the layout point x, y points now.", IpcMethodTraits.None),
            (ref IpcParams p, IpcReply reply) =>
            {
                var x = p.GetDouble("x");
                var y = p.GetDouble("y");
                if (p.Failed)
                {
                    return;
                }

                Aim(x, y);
                ReportCamera(reply);
            });
    }
}
