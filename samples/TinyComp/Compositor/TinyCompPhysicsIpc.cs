using System.Globalization;
using Basin.Ipc;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private void RegisterPhysicsCommands(IpcMethodRegistry methods)
    {
        _report.Register(methods, "tinycomp/physics-state", "physics-state", (ref IpcParams p, IpcReply reply) =>
        {
            if (p.Failed)
            {
                return;
            }

            _report.Line($"PHYSICS enabled={(PhysicsOn ? "yes" : "no")} engine={_config.Physics.Engine} frozen={(_physicsFrozen ? "yes" : "no")} bodies={_physicsBindings.Count}");
            foreach (var window in _physicsBindings.Keys)
            {
                _report.Line($"BODY {NameOf(window)} {PhysicsWhere(window)}");
            }

            WritePhysicsState(reply.Result);
        });

        _report.Register(methods, "tinycomp/physics-gravity", "gravity {value}", (ref IpcParams p, IpcReply reply) =>
        {
            if (!p.TryGetRaw("value", out var raw) || raw.IsEmpty)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'value' must be a number, cycle or config.");
                return;
            }

            var word = raw[0] == (byte)'"' && raw.Length >= 2
                ? System.Text.Encoding.UTF8.GetString(raw[1..^1])
                : System.Text.Encoding.UTF8.GetString(raw);
            double? number = null;
            if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
            {
                number = parsed;
                word = null;
            }

            switch (word)
            {
                case null when number is { } value:
                    SetGravity(value);
                    break;
                case "cycle":
                    CycleGravity();
                    break;
                case "config":
                    SetGravity(null);
                    break;
                default:
                    reply.Error(IpcErrorCodes.InvalidParams, "'value' must be a number, cycle or config.");
                    return;
            }

            var json = reply.Result;
            json.WriteStartObject();
            if (_gravityOverride is { } gravity)
            {
                json.WriteNumber("gravity", gravity);
            }
            else
            {
                json.WriteNull("gravity");
            }

            json.WriteEndObject();
        });

        _report.Register(methods, "tinycomp/physics-throw", "throw {vx:number} {vy:number} [{spin:number}] [{id:int}]", (ref IpcParams p, IpcReply reply) =>
        {
            var vx = p.GetDouble("vx");
            var vy = p.GetDouble("vy");
            var spin = p.TryGetDouble("spin", out var turn) ? turn : 0;
            var hasId = p.TryGetInt("id", out var id);
            if (p.Failed)
            {
                return;
            }

            var window = hasId ? FindWindowByToplevelId((ulong)id) : FocusedGrabTarget();
            if (window is null)
            {
                reply.Error(IpcErrorCodes.NotFound, hasId ? $"No window has id {id}. Use an id from the windows list." : "No window has focus. Give a window id.");
                return;
            }

            if (!ThrowWindow(window, vx, vy, spin))
            {
                reply.Error(IpcErrorCodes.InvalidParams, PhysicsOn ? "The window is not a dynamic body. Pick a window that physics moves." : "Physics is off. Turn it on with [physics] enable.");
            }
        });

        _report.Register(methods, "tinycomp/rotate", "rotate {angle:number} [{id:int}]", (ref IpcParams p, IpcReply reply) =>
            Rotate(ref p, reply, "angle", relative: false));

        _report.Register(methods, "tinycomp/rotate-by", "rotate-by {by:number} [{id:int}]", (ref IpcParams p, IpcReply reply) =>
            Rotate(ref p, reply, "by", relative: true));

        _report.Register(methods, "tinycomp/stop-spin", "stop-spin [{id:int}]", (ref IpcParams p, IpcReply reply) =>
        {
            var hasId = p.TryGetInt("id", out var id);
            if (p.Failed)
            {
                return;
            }

            if (TiltTarget(hasId, id, reply) is { } window)
            {
                _ = StopSpin(window);
            }
        });

        _report.Register(methods, "tinycomp/upright", "upright [{id:int}]", (ref IpcParams p, IpcReply reply) =>
        {
            var hasId = p.TryGetInt("id", out var id);
            if (p.Failed)
            {
                return;
            }

            if (TiltTarget(hasId, id, reply) is { } window)
            {
                UprightWindow(window);
            }
        });

        _report.Register(methods, "tinycomp/physics-freeze", "freeze {state:on|off}", (ref IpcParams p, IpcReply reply) =>
        {
            bool? frozen = p.TryGetBool("frozen", out var flag) ? flag : null;
            if (frozen is null && p.TryGetString("state", out var state))
            {
                frozen = state switch
                {
                    "on" => true,
                    "off" => false,
                    _ => null,
                };
            }

            if (p.Failed)
            {
                return;
            }

            if (frozen is not { } value)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'state' must be on or off.");
                return;
            }

            SetPhysicsFrozen(value);
        });
        }

    private void Rotate(ref IpcParams p, IpcReply reply, string key, bool relative)
    {
        var degrees = p.GetDouble(key);
        var hasId = p.TryGetInt("id", out var id);
        if (p.Failed)
        {
            return;
        }

        if (TiltTarget(hasId, id, reply) is { } window)
        {
            RotateWindow(window, degrees * Math.PI / 180, relative);
        }
    }

    private static void SetScaleFilter(IGrabTarget target, Basin.TextureFilter? filter, bool fromRule)
    {
        switch (target)
        {
            case Window window when window.Fit is { } fit:
                var wayland = fromRule ? window.Quirks.ScaleFilter : filter;
                fit.Filter = wayland;
                fit.Root.Filter = wayland;
                break;
            case XWindow xwindow:
                xwindow.Outer.Filter = fromRule ? xwindow.Quirks.ScaleFilter : filter;
                break;
        }
    }

    private IGrabTarget? TiltTarget(bool hasId, long id, IpcReply reply)
    {
        var window = hasId ? FindWindowByToplevelId((ulong)id) : FocusedGrabTarget();
        if (window is null)
        {
            reply.Error(IpcErrorCodes.NotFound, hasId ? $"No window has id {id}. Use an id from the windows list." : "No window has focus. Give a window id.");
        }

        return window;
    }
}
