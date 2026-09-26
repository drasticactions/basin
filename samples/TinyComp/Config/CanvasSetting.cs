using Basin;
using Basin.Config;
using Tomlyn.Model;

using Basin.Diagnostics;

namespace TinyComp;

internal sealed class CanvasSetting
{
    public const double DefaultZone = 0.12;

    public const double DefaultExtension = 0.5;

    public const double DefaultEdgeScale = 0.2;

    public const uint DefaultGridColor = 0x2a35c0ff;

    public const double DefaultSlope = 0.25;

    public bool? Enable { get; init; }

    public double? Zone { get; init; }

    public double? Extension { get; init; }

    public double? EdgeScale { get; init; }

    public double? Slope { get; init; }

    public int? MeshCell { get; init; }

    public CanvasGridMode? Grid { get; init; }

    public int? GridCell { get; init; }

    public uint? GridColor { get; init; }

    public int? AnimationMs { get; init; }

    public CanvasSide? Sides { get; init; }

    public double? CornerRadius { get; init; }

    public bool? CornerTaper { get; init; }

    public static CanvasSetting Defaults { get; } = new()
    {
        Enable = false,
        Zone = DefaultZone,
        Extension = DefaultExtension,
        EdgeScale = DefaultEdgeScale,
        Slope = DefaultSlope,
        MeshCell = 16,
        Grid = CanvasGridMode.Always,
        GridCell = 64,
        GridColor = DefaultGridColor,
        AnimationMs = 250,
        Sides = CanvasSide.Horizontal,
        CornerRadius = 1.0,
        CornerTaper = false,
    };

    public bool Enabled => Enable ?? false;

    public double ZoneFraction => Zone ?? DefaultZone;

    public double ExtensionFraction => Extension ?? DefaultExtension;

    public double EdgeScaleValue => EdgeScale ?? DefaultEdgeScale;

    public double SlopeValue => Slope ?? DefaultSlope;

    public int MeshCellSize => MeshCell ?? 16;

    public CanvasGridMode GridMode => Grid ?? CanvasGridMode.Always;

    public int GridCellSize => GridCell ?? 64;

    public uint GridRgba => GridColor ?? DefaultGridColor;

    public int AnimationMillis => AnimationMs ?? 250;

    public CanvasSide SideSet => Sides ?? CanvasSide.Horizontal;

    public double CornerRadiusValue => CornerRadius ?? 1.0;

    public bool CornerTaperValue => CornerTaper ?? false;

    public string CornerName => CornerTaperValue ? "taper" : CornerRadiusValue switch
    {
        1.0 => "round",
        0.0 => "square",
        _ => "radius",
    };

    public string SideNames => NamesOf(SideSet);

    public static string NamesOf(CanvasSide sides)
    {
        if (sides == CanvasSide.None)
        {
            return "none";
        }

        var names = new List<string>(4);
        if (sides.HasFlag(CanvasSide.Left))
        {
            names.Add("left");
        }

        if (sides.HasFlag(CanvasSide.Right))
        {
            names.Add("right");
        }

        if (sides.HasFlag(CanvasSide.Top))
        {
            names.Add("top");
        }

        if (sides.HasFlag(CanvasSide.Bottom))
        {
            names.Add("bottom");
        }

        return string.Join(',', names);
    }

    public RenderColor GridRenderColor
    {
        get
        {
            var rgba = GridRgba;
            var a = (rgba & 0xFF) / 255f;
            return new RenderColor(
                ((rgba >> 24) & 0xFF) / 255f * a,
                ((rgba >> 16) & 0xFF) / 255f * a,
                ((rgba >> 8) & 0xFF) / 255f * a,
                a);
        }
    }

    public CanvasSetting Over(CanvasSetting fallback) => new()
    {
        Enable = Enable ?? fallback.Enable,
        Zone = Zone ?? fallback.Zone,
        Extension = Extension ?? fallback.Extension,
        EdgeScale = EdgeScale ?? fallback.EdgeScale,
        Slope = Slope ?? fallback.Slope,
        MeshCell = MeshCell ?? fallback.MeshCell,
        Grid = Grid ?? fallback.Grid,
        GridCell = GridCell ?? fallback.GridCell,
        GridColor = GridColor ?? fallback.GridColor,
        AnimationMs = AnimationMs ?? fallback.AnimationMs,
        Sides = Sides ?? fallback.Sides,
        CornerRadius = CornerRadius ?? fallback.CornerRadius,
        CornerTaper = CornerTaper ?? fallback.CornerTaper,
    };

    public static CanvasSetting Parse(TomlTable table, string section, BasinLogger log)
    {
        bool? enable = null;
        double? zone = null;
        double? extension = null;
        double? edgeScale = null;
        double? slope = null;
        int? meshCell = null;
        CanvasGridMode? grid = null;
        int? gridCell = null;
        uint? gridColor = null;
        int? animation = null;
        CanvasSide? sides = null;
        double? corner = null;
        bool? taper = null;
        double? cornerRadius = null;
        foreach (var (key, value) in table)
        {
            switch (key)
            {
                case "enable" when value is bool flag:
                    enable = flag;
                    break;
                case "zone" when Fraction(value) is { } zoneValue:
                    zone = Math.Clamp(zoneValue, 0.0, 0.45);
                    break;
                case "extension" when Fraction(value) is { } extensionValue:
                    extension = Math.Max(0.0, extensionValue);
                    break;
                case "edge_scale" when Fraction(value) is { } edgeValue:
                    edgeScale = Math.Clamp(edgeValue, 0.01, 1.0);
                    break;
                case "slope" when Fraction(value) is { } slopeValue:
                    slope = Math.Clamp(slopeValue, Basin.Effects.CanvasWarp.MinSlope, 2.0);
                    break;
                case "mesh_cell" when value is long mesh:
                    meshCell = (int)Math.Clamp(mesh, 1, 4096);
                    break;
                case "grid" when value is string text:
                    grid = text switch
                    {
                        "always" => CanvasGridMode.Always,
                        "drag" => CanvasGridMode.Drag,
                        "never" => CanvasGridMode.Never,
                        _ => null,
                    };
                    if (grid is null)
                    {
                        log.Warn($"[{section}] grid \"{text}\" is not always|drag|never, ignored");
                    }

                    break;
                case "grid_cell" when value is long cell:
                    gridCell = (int)Math.Clamp(cell, 2, 4096);
                    break;
                case "grid_color" when TomlColor.Rgba(value) is { } rgba:
                    gridColor = rgba;
                    break;
                case "animation_ms" when value is long millis:
                    animation = (int)Math.Clamp(millis, 0, 10_000);
                    break;
                case "corner" when value is string shape:
                    corner = shape switch
                    {
                        "round" => 1.0,
                        "square" => 0.0,
                        _ => null,
                    };
                    taper = shape switch
                    {
                        "taper" => true,
                        "round" or "square" => false,
                        _ => null,
                    };
                    if (taper is null)
                    {
                        log.Warn($"[{section}] corner \"{shape}\" is not round|square|taper, ignored");
                    }

                    break;
                case "corner_radius" when Fraction(value) is { } radius:
                    cornerRadius = Math.Clamp(radius, 0.0, 1.0);
                    break;
                case "sides" when value is TomlArray list:
                    sides = ParseSides(list, section, log);
                    break;
                default:
                    log.Warn($"[{section}] {key}: unknown key or wrong type, ignored");
                    break;
            }
        }

        var parsed = new CanvasSetting
        {
            Enable = enable,
            Zone = zone,
            Extension = extension,
            EdgeScale = edgeScale,
            Slope = slope,
            MeshCell = meshCell,
            Grid = grid,
            GridCell = gridCell,
            GridColor = gridColor,
            AnimationMs = animation,
            Sides = sides,
            CornerRadius = cornerRadius ?? corner,
            CornerTaper = taper,
        };
        if (corner is { } shaped && cornerRadius is { } exact && shaped != exact)
        {
            log.Warn($"[{section}] corner_radius {exact} overrides corner");
        }

        return parsed.Constrained(section, log);
    }

    public CanvasSetting Constrained(string section, BasinLogger log)
    {
        var zone = ZoneFraction;
        var extension = ExtensionFraction;
        var edge = EdgeScaleValue;
        if (extension < zone)
        {
            log.Warn($"[{section}] extension {extension} is below zone {zone}, raising it to {zone}");
            extension = zone;
        }

        if (zone > 0 && extension > 0 && edge >= zone / extension)
        {
            var clamped = Basin.Effects.CanvasWarp.ClampEdgeScale(edge, (int)Math.Round(zone * 10000), (int)Math.Round(extension * 10000));
            log.Warn($"[{section}] edge_scale {edge} must stay below zone / extension = {zone / extension:F3}, clamping it to {clamped:F3}");
            edge = clamped;
        }

        if (extension == ExtensionFraction && edge == EdgeScaleValue)
        {
            return this;
        }

        return new CanvasSetting
        {
            Enable = Enable,
            Zone = Zone,
            Extension = Extension is null && extension == ExtensionFraction ? null : extension,
            EdgeScale = EdgeScale is null && edge == EdgeScaleValue ? null : edge,
            Slope = Slope,
            MeshCell = MeshCell,
            Grid = Grid,
            GridCell = GridCell,
            GridColor = GridColor,
            AnimationMs = AnimationMs,
            Sides = Sides,
            CornerRadius = CornerRadius,
            CornerTaper = CornerTaper,
        };
    }

    private static CanvasSide ParseSides(TomlArray list, string section, BasinLogger log)
    {
        var sides = CanvasSide.None;
        foreach (var item in list)
        {
            var side = (item as string) switch
            {
                "left" => CanvasSide.Left,
                "right" => CanvasSide.Right,
                "top" => CanvasSide.Top,
                "bottom" => CanvasSide.Bottom,
                _ => CanvasSide.None,
            };
            if (side == CanvasSide.None)
            {
                log.Warn($"[{section}] sides: \"{item}\" is not left|right|top|bottom, ignored");
            }

            sides |= side;
        }

        if (sides == CanvasSide.None)
        {
            log.Warn($"[{section}] sides is empty: the canvas has no zones");
        }

        return sides;
    }

    private static double? Fraction(object value) => value switch
    {
        double fraction => fraction,
        long whole => whole,
        _ => null,
    };
}
