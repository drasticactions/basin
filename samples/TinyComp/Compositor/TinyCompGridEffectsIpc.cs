using System.Globalization;
using System.Text;
using Basin.Host;
using Basin.Ipc;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private GridEffectsSetting? _gridPreset;

    private string? _gridPresetName;

    private void RegisterGridEffectsCommands(IpcMethodRegistry methods)
    {
        _report.Register(methods, "tinycomp/grid-effects", "grid-effects {preset:curved|off}", (ref IpcParams p, IpcReply reply) =>
        {
            if (!p.TryGetString("preset", out var name))
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'preset' must be curved or off.");
                return;
            }

            if (name == "off")
            {
                _gridPreset = null;
                _gridPresetName = null;
            }
            else if (GridEffectsPresets.Named(name) is { } preset)
            {
                _gridPreset = preset;
                _gridPresetName = name;
            }
            else
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"No grid effects preset is named {name}. Use curved or off.");
                return;
            }

            LayoutCanvasAll();
            ScheduleEffectRepaint();
            _report.Line($"GRID-EFFECTS preset={_gridPresetName ?? "none"}");
            var json = reply.Result;
            json.WriteStartObject();
            json.WriteString("preset", _gridPresetName ?? "none");
            json.WriteEndObject();
        });

        _report.Register(methods, "tinycomp/grid-state", "grid-state", (ref IpcParams p, IpcReply reply) =>
        {
            if (p.Failed)
            {
                return;
            }

            var json = reply.Result;
            json.WriteStartObject();
            json.WriteStartArray("views");
            for (var i = 0; i < Views.Count; i++)
            {
                if (Views[i] is { Tag: OutputPolicy } view)
                {
                    _report.Line(GridStateLine(view));
                    WriteGridState(json, view);
                }
            }

            json.WriteEndArray();
            json.WriteEndObject();
        });
    }

    private static string GridViewName(OutputView view) =>
        view.Canvas.Step is not null ? "step" : view.Canvas.Overview ? "slope" : "canvas";

    private string GridGlowState(OutputView view) =>
        view.Canvas.Step is not null ? OverviewOf(view).StepGlow.State : view.Canvas.Glow.State;

    private static string PinchZoneName(OutputView view, CanvasSide side)
    {
        var name = side switch
        {
            CanvasSide.Left => "left",
            CanvasSide.Right => "right",
            CanvasSide.Top => "top",
            _ => "bottom",
        };
        return view.Canvas.Step is not null ? name + "-wall" : name;
    }

    private string GridStateLine(OutputView view)
    {
        var canvas = view.Canvas;
        var feed = canvas.Lights;
        var settings = canvas.Settings.GridEffectsValue;
        var pinch = new StringBuilder();
        foreach (var side in PinchZones)
        {
            if (!PinchZoneActive(view, side))
            {
                continue;
            }

            var zone = SideIndex(side);
            if (pinch.Length > 0)
            {
                pinch.Append(',');
            }

            pinch.Append(CultureInfo.InvariantCulture, $"{PinchZoneName(view, side)}:{canvas.Pinch.DepthOf(zone):F3}@{canvas.Pinch.CenterOf(zone):F1}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"GRID output={view.Output.Name} view={GridViewName(view)} shown={(GridShown(view) ? "yes" : "no")} glow={GridGlowState(view)}"
            + $" lights={feed.Lit} fading={feed.Fading}"
            + $" pointer={feed.PointerStrength:F2} density={(settings.DensityValue ? "on" : "off")}"
            + $" pinch={(pinch.Length == 0 ? "none" : pinch.ToString())} generation={canvas.Generation} preset={_gridPresetName ?? "none"}");
    }

    private void WriteGridState(System.Text.Json.Utf8JsonWriter json, OutputView view)
    {
        var canvas = view.Canvas;
        var feed = canvas.Lights;
        var lights = feed.Lights;
        json.WriteStartObject();
        json.WriteString("output", view.Output.Name);
        json.WriteString("view", GridViewName(view));
        json.WriteBoolean("shown", GridShown(view));
        json.WriteString("glow", GridGlowState(view));
        json.WriteNumber("lights", feed.Lit);
        json.WriteNumber("fading", feed.Fading);
        json.WriteNumber("pointer", feed.PointerStrength);
        json.WriteBoolean("density", canvas.Settings.GridEffectsValue.DensityValue);
        json.WriteNumber("generation", canvas.Generation);
        json.WriteString("preset", _gridPresetName ?? "none");
        json.WriteStartArray("boxes");
        for (var slot = 0; slot < Basin.Effects.GridLights.Capacity; slot++)
        {
            var light = lights[slot];
            json.WriteStartObject();
            json.WriteNumber("x", light.X);
            json.WriteNumber("y", light.Y);
            json.WriteNumber("width", light.Width);
            json.WriteNumber("height", light.Height);
            json.WriteNumber("strength", light.Strength);
            json.WriteString("source", feed.KeyAt(slot) is IGrabTarget window ? NameOf(window) : "none");
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("pinch");
        foreach (var side in PinchZones)
        {
            if (!PinchZoneActive(view, side))
            {
                continue;
            }

            var zone = SideIndex(side);
            json.WriteStartObject();
            json.WriteString("zone", PinchZoneName(view, side));
            json.WriteNumber("depth", canvas.Pinch.DepthOf(zone));
            json.WriteNumber("center", canvas.Pinch.CenterOf(zone));
            json.WriteString("source", canvas.Pinch.SourceOf(zone) is IGrabTarget source ? NameOf(source) : "none");
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }
}
