using System.Globalization;
using System.Numerics;
using Basin;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Ipc;
using Basin.Scene;
using Basin.Scene3D;

namespace DungeonWm;

internal sealed partial class DungeonWm
{
    private static readonly string[] EventNames = ["dungeonwm/room-entered", "dungeonwm/window-hung", "dungeonwm/window-detached", "dungeonwm/takeover", "dungeonwm/mode", "dungeonwm/audio"];

    private readonly IpcLineReport _report = new();
    private IpcServer? _ipc;
    private FrameSettle? _settle;

    private IpcServer? StartIpc()
    {
        _settle = new FrameSettle(_outputs);
        _ipc = _options.Ipc.Attach(_host.Loop, _services, _host.Socket, new IpcSessionInfo
        {
            Compositor = "dungeon-wm",
            Backend = _options.Backend.ToString().ToLowerInvariant(),
            Renderer = RendererName,
            XwaylandDisplay = () => _xServer?.DisplayName,
            Quit = Stop,
            Settle = _settle,
            Processes = _processes,
        });
        _ipc.SyntheticInput = _injector;
        foreach (var name in EventNames)
        {
            _ipc.Events.Declare(name);
        }

        RegisterCommands(_ipc.Methods);
        _ipc.Start();
        _ipc.StartLineFront();
        return _ipc;
    }

    private void RaiseEvent(string name)
    {
        if (_ipc is not { } ipc || !ipc.Events.HasSubscribers(name))
        {
            return;
        }

        var json = ipc.Events.BeginEvent(name);
        json.WriteStartObject();
        json.WriteNumber("room"u8, (_workspaces.Active?.Workspace ?? -1) + 1);
        json.WriteString("mode"u8, ModeName);
        if (_takeover is { } takeover)
        {
            json.WriteString("takeover"u8, takeover.AppId);
        }

        if (_focused is { } focused)
        {
            json.WriteString("focused"u8, focused.AppId);
        }

        json.WriteEndObject();
        ipc.Events.EndEvent();
    }

    private void RegisterCommands(IpcMethodRegistry methods)
    {
        _report.AddInputLines(methods);
        _report.AddWindowLines(methods);
        _report.AddSessionLines(methods);

        _report.Register(methods, "dungeonwm/shot", "shot {path}",
            Describe("Write the composited output under the pointer, world included, to a PNG.", IpcMethodTraits.ReadOnly),
            (ref IpcParams p, IpcReply _) =>
            {
                var path = p.GetString("path");
                if (!p.Failed)
                {
                    WriteScreenshot(path);
                }
            });

        _report.Register(methods, "dungeonwm/shotraw", "shotraw {path}",
            Describe("Write the last buffer the first output presented, as scanned out, to a PNG.", IpcMethodTraits.ReadOnly),
            (ref IpcParams p, IpcReply _) =>
            {
                var path = p.GetString("path");
                if (!p.Failed)
                {
                    WritePresented(path);
                }
            });

        _report.Register(methods, "dungeonwm/where", "where",
            Describe("Print the camera, the room, the focus, the windows and the outputs.", IpcMethodTraits.ReadOnly),
            PrintState);

        _report.Register(methods, "dungeonwm/camera", "camera",
            Describe("Get the camera pose, or set it with x, y, z, yaw and pitch in meters and degrees.", IpcMethodTraits.Idempotent),
            (ref IpcParams p, IpcReply reply) =>
            {
                if (p.TryGetDouble("x", out var x) && p.TryGetDouble("y", out var y) && p.TryGetDouble("z", out var z))
                {
                    var yaw = p.TryGetDouble("yaw", out var yawDegrees) ? (float)(yawDegrees * Math.PI / 180) : _camera.Yaw;
                    var pitch = p.TryGetDouble("pitch", out var pitchDegrees) ? (float)(pitchDegrees * Math.PI / 180) : _camera.Pitch;
                    _walker.Teleport(new Vector3((float)x, (float)y, (float)z), yaw, pitch);
                    TrackRoom();
                    ScheduleAll();
                }

                ReportCamera(reply);
            });

        _report.Register(methods, "dungeonwm/walk", "walk {dx:number} {dz:number}",
            Describe("Walk dx meters to the right and dz meters forward, with collision.", IpcMethodTraits.None),
            (ref IpcParams p, IpcReply reply) =>
            {
                var dx = p.GetDouble("dx");
                var dz = p.GetDouble("dz");
                if (p.Failed)
                {
                    return;
                }

                WalkBy((float)dx, (float)dz);
                ReportCamera(reply);
            });

        _report.Register(methods, "dungeonwm/look", "look {yaw:number} {pitch:number}",
            Describe("Turn the camera by yaw and pitch degrees.", IpcMethodTraits.None),
            (ref IpcParams p, IpcReply reply) =>
            {
                var yaw = p.GetDouble("yaw");
                var pitch = p.GetDouble("pitch");
                if (p.Failed)
                {
                    return;
                }

                _walker.Look((float)(yaw * Math.PI / 180), (float)(pitch * Math.PI / 180));
                ScheduleAll();
                ReportCamera(reply);
            });

        _report.Register(methods, "dungeonwm/teleport", "tp {room:int}",
            Describe("Move the camera to the entrance of a room, numbered from 1.", IpcMethodTraits.Idempotent),
            (ref IpcParams p, IpcReply reply) =>
            {
                var number = (int)p.GetInt("room");
                if (p.Failed)
                {
                    return;
                }

                if (RoomByNumber(number) is not { } room)
                {
                    p.Invalid($"no room {number}");
                    return;
                }

                _walker.Teleport(_world.EntranceOf(room, _settings.EyeHeight), _world.EntranceYaw(room));
                TrackRoom();
                ScheduleAll();
                ReportCamera(reply);
            });

        _report.Register(methods, "dungeonwm/hang", "hang {id} {room:int} {slot}",
            Describe("Hang a window (toplevel id or app id) in a room on a slot named side:along, or any.", IpcMethodTraits.None),
            (ref IpcParams p, IpcReply _) =>
            {
                var id = p.GetString("id");
                var number = (int)p.GetInt("room");
                var slotName = p.GetString("slot");
                if (p.Failed)
                {
                    return;
                }

                if (FindWindow(id) is not { } window)
                {
                    p.Invalid($"no window {id}");
                    return;
                }

                if (RoomByNumber(number) is not { } room)
                {
                    p.Invalid($"no room {number}");
                    return;
                }

                WallSlot? slot = null;
                if (slotName != "any")
                {
                    slot = ParseSlot(room, slotName);
                    if (slot is null)
                    {
                        p.Invalid($"no slot {slotName} in room {number}");
                        return;
                    }
                }

                HangNow(window, room, slot);
            });

        _report.Register(methods, "dungeonwm/takeover", "takeover {target}",
            Describe("Show one window flat at 1:1 on its output, or off to return to the world.", IpcMethodTraits.None),
            (ref IpcParams p, IpcReply _) =>
            {
                var target = p.GetString("target");
                if (p.Failed)
                {
                    return;
                }

                if (target == "off")
                {
                    LeaveTakeover();
                    return;
                }

                if (FindWindow(target) is not { } window)
                {
                    p.Invalid($"no window {target}");
                    return;
                }

                EnterTakeover(window);
            });

        _report.Register(methods, "dungeonwm/face", "face {id}",
            Describe("Stand in front of a window where it is seen at 1:1, centered on the output under the pointer.", IpcMethodTraits.Idempotent),
            (ref IpcParams p, IpcReply reply) =>
            {
                var id = p.GetString("id");
                if (p.Failed)
                {
                    return;
                }

                if (FindWindow(id) is not { } window)
                {
                    p.Invalid($"no window {id}");
                    return;
                }

                FaceWindow(window, fly: false);
                ReportCamera(reply);
            });

        _report.Register(methods, "dungeonwm/world", "world",
            Describe("Print the rooms, their slots, the corridors and the props.", IpcMethodTraits.ReadOnly),
            PrintWorld);

        _report.Register(methods, "dungeonwm/reload", "reload",
            Describe("Reread the configuration file, as SIGHUP does, and print a RELOAD line naming the keys that need a restart.", IpcMethodTraits.Idempotent),
            Reload);

        _report.Register(methods, "dungeonwm/audio", "audio",
            Describe("Print the audio mode and one VOICE line for each window that plays: its direction, distance, path, gain, cutoff and reverb.", IpcMethodTraits.ReadOnly),
            PrintAudio);

        _report.Register(methods, "dungeonwm/sounds", "sounds",
            Describe("Print whether the sound effects play, how many have played, and the count of each kind: steps, jumps, landings, impacts, hangs and plaque clicks.", IpcMethodTraits.ReadOnly),
            PrintSounds);

        _report.Register(methods, "dungeonwm/audio-mode", "audio-mode {mode}",
            Describe("Set the audio mode to auto, hrtf, pan or off until the next reload. Every voice is built again.", IpcMethodTraits.Idempotent),
            (ref IpcParams p, IpcReply _) =>
            {
                var mode = p.GetString("mode");
                if (p.Failed)
                {
                    return;
                }

                if (Array.IndexOf(AudioSettings.Modes, mode) < 0)
                {
                    p.Invalid($"no audio mode {mode}: expected auto, hrtf, pan or off");
                    return;
                }

                _audioModeOverride = mode;
                RestartAudio();
                PrintAudio();
            });

        RegisterPhysicsCommands(methods);
        RegisterPlaqueCommands(methods);
        RegisterCrosshairCommands(methods);
    }

    private void FaceWindow(DungeonWmWindow window, bool fly)
    {
        var (position, yaw, pitch) = OneToOnePose(window);
        if (fly)
        {
            _walker.FlyTo(position, yaw, pitch);
        }
        else
        {
            _walker.Teleport(position, yaw, pitch);
        }

        TrackRoom();
        ScheduleAll();
    }

    private (Vector3 Position, float Yaw, float Pitch) OneToOnePose(DungeonWmWindow window)
    {
        var view = OutputUnderPointer() ?? _outputs.Views[0];
        var union = _layout.Bounds;
        var metersPerPixel = window.World.SurfaceHeight > 0 ? window.World.Height / window.World.SurfaceHeight : 0.001f;
        var distance = union.Height * metersPerPixel / (2 * MathF.Tan(_camera.FieldOfView / 2));
        var slice = _layout.BoxOf(view.Output);
        var offsetX = (slice.X + (slice.Width / 2.0) - (union.X + (union.Width / 2.0))) * metersPerPixel;
        var offsetY = (slice.Y + (slice.Height / 2.0) - (union.Y + (union.Height / 2.0))) * metersPerPixel;
        var normal = window.World.Normal;
        var right = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitY));
        var position = window.World.Position + (normal * (distance - window.World.Recess)) - (right * (float)offsetX) + (Vector3.UnitY * (float)offsetY);
        var yaw = MathF.Atan2(normal.X, normal.Z);
        return (position, yaw, 0);
    }

    private static string PickDetail(in WorldHit hit) => hit.Kind switch
    {
        WorldHitKind.Frame => $" part={hit.Part.ToString().ToLowerInvariant()}",
        WorldHitKind.Plaque => $" button={PlaqueLayout.Name(PlaqueAt(hit))}",
        _ => string.Empty,
    };

    private static string FrameName(DungeonWmWindow window) =>
        window.World.ShownFrame is null ? "none" : window.FrameStyle?.Name ?? "none";

    private static IpcMethodInfo Describe(string description, IpcMethodTraits traits) => new(description, null, traits);

    private void ReportCamera(IpcReply reply)
    {
        var position = _camera.Position;
        _report.Line(string.Create(CultureInfo.InvariantCulture,
            $"CAMERA {position.X:F3} {position.Y:F3} {position.Z:F3} yaw={_camera.Yaw * 180 / MathF.PI:F1} pitch={_camera.Pitch * 180 / MathF.PI:F1} room={(_workspaces.Active?.Workspace ?? -1) + 1}"));
        var json = reply.Result;
        json.WriteStartObject();
        json.WriteNumber("x"u8, position.X);
        json.WriteNumber("y"u8, position.Y);
        json.WriteNumber("z"u8, position.Z);
        json.WriteNumber("yaw"u8, _camera.Yaw * 180 / MathF.PI);
        json.WriteNumber("pitch"u8, _camera.Pitch * 180 / MathF.PI);
        json.WriteNumber("room"u8, (_workspaces.Active?.Workspace ?? -1) + 1);
        json.WriteEndObject();
    }

    private void WalkBy(float right, float forward)
    {
        _walker.WalkBy(new Vector2(right, forward), _world.World);
        TrackRoom();
        ScheduleAll();
    }

    private MapRegion? RoomByNumber(int number)
    {
        foreach (var room in _world.Map.Rooms)
        {
            if (!room.Removed && room.Workspace + 1 == number)
            {
                return room;
            }
        }

        return null;
    }

    private DungeonWmWindow? FindWindow(string id)
    {
        if (ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            foreach (var window in _windows)
            {
                if (window.ToplevelId == number)
                {
                    return window;
                }
            }
        }

        foreach (var window in _windows)
        {
            if (window.AppId == id || window.Title == id)
            {
                return window;
            }
        }

        return null;
    }

    private WallSlot? ParseSlot(MapRegion room, string name)
    {
        var parts = name.Split(':');
        if (parts.Length != 2 || !Enum.TryParse<GridSide>(parts[0], ignoreCase: true, out var side) ||
            !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var along))
        {
            return null;
        }

        return _world.FindSlot(room, (side, along));
    }

    private void HangNow(DungeonWmWindow window, MapRegion room, WallSlot? slot)
    {
        UnbindBody(window);
        if (slot is { } wanted && _world.Occupant(wanted) is { } occupant && !ReferenceEquals(occupant, window))
        {
            _world.Release(occupant);
            Hang(occupant, room, null);
        }

        Hang(window, room, slot);
        if (!StartHang(window))
        {
            BasinReport.Line($"HUNG {window.AppId} room={room.Workspace + 1} slot={SlotName(window.Slot)}");
            SoundHung(window);
            RaiseEvent("dungeonwm/window-hung");
        }

        ScheduleAll();
    }

    private void PrintState()
    {
        var position = _camera.Position;
        _report.Line(string.Create(CultureInfo.InvariantCulture,
            $"CAMERA {position.X:F3} {position.Y:F3} {position.Z:F3} yaw={_camera.Yaw * 180 / MathF.PI:F1} pitch={_camera.Pitch * 180 / MathF.PI:F1} room={(_workspaces.Active?.Workspace ?? -1) + 1}"));
        _report.Line($"POINTER {_pointer.X:F1} {_pointer.Y:F1} looking={(_looking ? "yes" : "no")} mode={ModeName}");
        _report.Line($"FOCUS {_focused?.AppId ?? "none"} pointer={(_seat.Pointer.Focus is null ? "none" : WindowOf(_seat.Pointer.Focus)?.AppId ?? "surface")}");
        _report.Line($"TAKEOVER {_takeover?.AppId ?? "off"} locked={(_locked ? "yes" : "no")}");
        foreach (var window in _windows)
        {
            var at = window.World.Position;
            _report.Line(string.Create(CultureInfo.InvariantCulture,
                $"WINDOW {window.ToplevelId} {window.AppId} state={window.State.ToString().ToLowerInvariant()} room={(window.Room?.Workspace ?? -1) + 1} slot={SlotName(window.Slot)} size={window.World.SurfaceWidth}x{window.World.SurfaceHeight} meters={window.World.Width:F3}x{window.World.Height:F3} at={at.X:F2},{at.Y:F2},{at.Z:F2} layers={window.World.LayerCount} frame={FrameName(window)}"));
        }

        foreach (var snapshot in _snapshots)
        {
            var at = snapshot.World.Position;
            _report.Line(string.Create(CultureInfo.InvariantCulture,
                $"SNAPSHOT {snapshot.AppId} state={(snapshot.Fading < 0 ? "falling" : "fading")} room={(snapshot.Room?.Workspace ?? -1) + 1} at={at.X:F2},{at.Y:F2},{at.Z:F2} opacity={snapshot.World.Opacity:F2} canvas={(snapshot.CanvasTexture is null ? "none" : "copy")}"));
        }

        foreach (var (view, output) in _worldOutputs)
        {
            _report.Line($"OUTPUT {view.Output.Name} box={_layout.BoxOf(view.Output)} frames={output.Frames} visible-rooms={output.Visible.Rooms.Count} visible-windows={output.Visible.Windows.Count} surfaces={output.Visible.Surfaces.Count}");
        }

        var hit = PickAt(_pointer.X, _pointer.Y);
        _report.Line(string.Create(CultureInfo.InvariantCulture,
            $"PICK {hit.Kind.ToString().ToLowerInvariant()} window={(hit.Window?.Tag as DungeonWmWindow)?.AppId ?? "none"} at={hit.SurfaceX:F1},{hit.SurfaceY:F1}{PickDetail(hit)}"));
        PrintPhysics();
    }

    private void PrintWorld()
    {
        foreach (var room in _world.Map.Rooms)
        {
            var slots = _world.SlotsOf(room).ToList();
            var free = slots.Count(_world.IsFree);
            _report.Line($"ROOM {room.Workspace + 1} {room.Width}x{room.Depth} at={room.X0},{room.Z0} slots={slots.Count} free={free} removed={(room.Removed ? "yes" : "no")} lights={_world.RoomOf(room).Lights.Count} triangles={_world.RoomOf(room).Structure?.TriangleCount ?? 0}");
        }

        _report.Line($"CORRIDORS {_world.Map.Regions.Count(r => r.Kind == RegionKind.Corridor)} doorways={_world.Map.Doorways.Count}");
        _report.Line($"PROPS {_world.Props.Count}");
    }

    private void WriteScreenshot(string? path)
    {
        if (path is not { Length: > 0 } || (OutputUnderPointer() ?? _outputs.Views.FirstOrDefault()) is not { } view)
        {
            return;
        }

        _report.Line(SceneScreenshot.Write(_scene, _renderer, view.Output, path)
            ? $"SHOT {path} {view.Output.Name}"
            : $"SHOT failed {view.Output.Name}");
    }

    private void WritePresented(string path)
    {
        var view = _outputs.Views.FirstOrDefault();
        var blit = view?.Output is IHardwareCursor hardware && hardware.TryPresentedCursor(out var sprite, out var where)
            ? new CursorBlit(sprite, where)
            : default;
        var buffer = view?.LastPresentedBuffer;
        _report.Line(SceneScreenshot.WritePresented(buffer, view?.Scene?.PresentedLayers, blit, _renderer, path, out var planes) switch
        {
            ScreenshotOutcome.NoFrame => "SHOTRAW none",
            ScreenshotOutcome.Unreadable => $"SHOTRAW unreadable {buffer!.Width}x{buffer.Height}",
            _ => $"SHOTRAW {path} {buffer!.Width}x{buffer.Height} planes={planes} direct={(view?.Scene?.IsDirectScanout == true ? "yes" : "no")}",
        });
    }

    private partial void RegisterPhysicsCommands(IpcMethodRegistry methods);

    private partial void PrintPhysics();

    private partial void UnbindBody(DungeonWmWindow window);
}
