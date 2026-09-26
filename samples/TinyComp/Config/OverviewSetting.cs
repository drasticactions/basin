using Basin;
using Basin.Config;
using Basin.Seat;
using Tomlyn.Model;

using Basin.Diagnostics;

namespace TinyComp;

internal sealed class OverviewSetting
{
    public const double DefaultScale = 0.75;

    public const double MinScale = 0.3;

    public const double MaxScale = 0.95;

    public const double DefaultThresholdIn = 0.667;

    public const double DefaultThresholdOut = 0.333;

    public const int DefaultFingers = 4;

    public const int DefaultHotCornerMs = 150;

    public const int WorkspaceSwipeFingers = 3;

    public const double DefaultWallWidth = 0.04;

    public const double MinWallWidth = 0.005;

    public const double MaxWallWidth = 0.2;

    public const uint DefaultWallColor = 0x262a3aff;

    public const double DefaultWallShade = 0.25;

    public const double MaxWallShade = 0.8;

    public bool? Enable { get; init; }

    public double? Scale { get; init; }

    public double? ThresholdIn { get; init; }

    public double? ThresholdOut { get; init; }

    public bool? Gesture { get; init; }

    public int? GestureFingers { get; init; }

    public ScreenCorner? HotCorner { get; init; }

    public int? HotCornerMs { get; init; }

    public OverviewWall? Wall { get; init; }

    public double? WallWidth { get; init; }

    public uint? WallColor { get; init; }

    public double? WallShade { get; init; }

    public static OverviewSetting Defaults { get; } = new()
    {
        Enable = true,
        Scale = DefaultScale,
        ThresholdIn = DefaultThresholdIn,
        ThresholdOut = DefaultThresholdOut,
        Gesture = true,
        GestureFingers = DefaultFingers,
        HotCorner = ScreenCorner.TopLeft,
        HotCornerMs = DefaultHotCornerMs,
        Wall = OverviewWall.Slope,
        WallWidth = DefaultWallWidth,
        WallColor = DefaultWallColor,
        WallShade = DefaultWallShade,
    };

    public bool Enabled => Enable ?? true;

    public double ScaleValue => Scale ?? DefaultScale;

    public double ThresholdInValue => ThresholdIn ?? DefaultThresholdIn;

    public double ThresholdOutValue => ThresholdOut ?? DefaultThresholdOut;

    public bool GestureEnabled => Gesture ?? true;

    public int FingerCount => GestureFingers ?? DefaultFingers;

    public ScreenCorner HotCornerValue => HotCorner ?? ScreenCorner.TopLeft;

    public string HotCornerName => NameOf(HotCornerValue);

    public int HotCornerMillis => HotCornerMs ?? DefaultHotCornerMs;

    public OverviewWall WallValue => Wall ?? OverviewWall.Slope;

    public bool Steps => WallValue == OverviewWall.Step;

    public string WallName => NameOf(WallValue);

    public double WallWidthValue => WallWidth ?? DefaultWallWidth;

    public uint WallRgba => WallColor ?? DefaultWallColor;

    public double WallShadeValue => WallShade ?? DefaultWallShade;

    public RenderColor WallRenderColor
    {
        get
        {
            var rgba = WallRgba;
            var a = (rgba & 0xFF) / 255f;
            return new RenderColor(
                ((rgba >> 24) & 0xFF) / 255f * a,
                ((rgba >> 16) & 0xFF) / 255f * a,
                ((rgba >> 8) & 0xFF) / 255f * a,
                a);
        }
    }

    public static string NameOf(OverviewWall wall) => wall == OverviewWall.Step ? "step" : "slope";

    public static OverviewWall? WallFromName(string name) => name switch
    {
        "slope" => OverviewWall.Slope,
        "step" => OverviewWall.Step,
        _ => null,
    };

    public static string NameOf(ScreenCorner corner) => corner switch
    {
        ScreenCorner.TopLeft => "top-left",
        ScreenCorner.TopRight => "top-right",
        ScreenCorner.BottomLeft => "bottom-left",
        ScreenCorner.BottomRight => "bottom-right",
        _ => "none",
    };

    public ShelfScales ShelfScalesFor(CanvasSetting canvas)
    {
        var scales = canvas.ShelfScaleValues;
        var top = ScaleValue;
        return new ShelfScales(
            Math.Min(scales.Left, top), Math.Min(scales.Right, top), Math.Min(scales.Top, top), Math.Min(scales.Bottom, top));
    }

    public OverviewSetting Over(OverviewSetting fallback) => new()
    {
        Enable = Enable ?? fallback.Enable,
        Scale = Scale ?? fallback.Scale,
        ThresholdIn = ThresholdIn ?? fallback.ThresholdIn,
        ThresholdOut = ThresholdOut ?? fallback.ThresholdOut,
        Gesture = Gesture ?? fallback.Gesture,
        GestureFingers = GestureFingers ?? fallback.GestureFingers,
        HotCorner = HotCorner ?? fallback.HotCorner,
        HotCornerMs = HotCornerMs ?? fallback.HotCornerMs,
        Wall = Wall ?? fallback.Wall,
        WallWidth = WallWidth ?? fallback.WallWidth,
        WallColor = WallColor ?? fallback.WallColor,
        WallShade = WallShade ?? fallback.WallShade,
    };

    public static OverviewSetting Parse(TomlTable table, string section, BasinLogger log)
    {
        bool? enable = null;
        double? scale = null;
        double? thresholdIn = null;
        double? thresholdOut = null;
        bool? gesture = null;
        int? fingers = null;
        ScreenCorner? corner = null;
        int? cornerMs = null;
        OverviewWall? wall = null;
        double? wallWidth = null;
        uint? wallColor = null;
        double? wallShade = null;
        foreach (var (key, value) in table)
        {
            switch (key)
            {
                case "enable" when value is bool flag:
                    enable = flag;
                    break;
                case "scale" when Fraction(value) is { } zoom:
                    scale = Math.Clamp(zoom, MinScale, MaxScale);
                    break;
                case "threshold_in" when Fraction(value) is { } inward:
                    thresholdIn = Math.Clamp(inward, 0.0, 1.0);
                    break;
                case "threshold_out" when Fraction(value) is { } outward:
                    thresholdOut = Math.Clamp(outward, 0.0, 1.0);
                    break;
                case "gesture" when value is bool swipe:
                    gesture = swipe;
                    break;
                case "gesture_fingers" when value is long count:
                    var clamped = (int)Math.Clamp(count, 3, 5);
                    if (clamped == WorkspaceSwipeFingers)
                    {
                        log.Warn($"[{section}] gesture_fingers {clamped} is the workspace swipe's count, keeping {DefaultFingers}");
                    }
                    else
                    {
                        fingers = clamped;
                    }

                    break;
                case "hot_corner" when value is string name:
                    corner = name switch
                    {
                        "top-left" => ScreenCorner.TopLeft,
                        "top-right" => ScreenCorner.TopRight,
                        "bottom-left" => ScreenCorner.BottomLeft,
                        "bottom-right" => ScreenCorner.BottomRight,
                        "none" => ScreenCorner.None,
                        _ => null,
                    };
                    if (corner is null)
                    {
                        log.Warn($"[{section}] hot_corner \"{name}\" is not top-left|top-right|bottom-left|bottom-right|none, ignored");
                    }

                    break;
                case "hot_corner_ms" when value is long millis:
                    cornerMs = (int)Math.Clamp(millis, 0, 10_000);
                    break;
                case "wall" when value is string wallName:
                    wall = WallFromName(wallName);
                    if (wall is null)
                    {
                        log.Warn($"[{section}] wall \"{wallName}\" is not slope|step, keeping slope");
                    }

                    break;
                case "wall_width" when Fraction(value) is { } width:
                    wallWidth = Math.Clamp(width, MinWallWidth, MaxWallWidth);
                    break;
                case "wall_color" when TomlColor.Rgba(value) is { } rgba:
                    wallColor = rgba;
                    break;
                case "wall_shade" when Fraction(value) is { } shade:
                    wallShade = Math.Clamp(shade, 0.0, MaxWallShade);
                    break;
                default:
                    log.Warn($"[{section}] {key}: unknown key or wrong type, ignored");
                    break;
            }
        }

        if (thresholdIn is not null || thresholdOut is not null)
        {
            var inward = thresholdIn ?? DefaultThresholdIn;
            var outward = thresholdOut ?? DefaultThresholdOut;
            if (outward >= inward)
            {
                log.Warn($"[{section}] threshold_out {outward} must be below threshold_in {inward}, swapping them");
                (thresholdIn, thresholdOut) = (outward, inward);
            }
        }

        return new OverviewSetting
        {
            Enable = enable,
            Scale = scale,
            ThresholdIn = thresholdIn,
            ThresholdOut = thresholdOut,
            Gesture = gesture,
            GestureFingers = fingers,
            HotCorner = corner,
            HotCornerMs = cornerMs,
            Wall = wall,
            WallWidth = wallWidth,
            WallColor = wallColor,
            WallShade = wallShade,
        };
    }

    private static double? Fraction(object value) => value switch
    {
        double fraction => fraction,
        long whole => whole,
        _ => null,
    };
}
