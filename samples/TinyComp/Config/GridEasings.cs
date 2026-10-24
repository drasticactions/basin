using System.Globalization;
using Basin.Effects;
using Tomlyn.Model;

namespace TinyComp;

internal static class GridEasings
{
    public static readonly string[] Names =
        ["linear", "sigmoid", "circle", "in-curve", "out-curve", "in-cubic", "out-cubic", "in-out-cubic", "in-out-sine"];

    public static EasingCurve? FromName(string name) => name switch
    {
        "linear" => EasingCurve.Linear,
        "sigmoid" => EasingCurve.Sigmoid,
        "circle" => EasingCurve.Circle,
        "in-curve" => EasingCurve.InCurve,
        "out-curve" => EasingCurve.OutCurve,
        "in-cubic" => EasingCurve.InCubic,
        "out-cubic" => EasingCurve.OutCubic,
        "in-out-cubic" => EasingCurve.InOutCubic,
        "in-out-sine" => EasingCurve.InOutSine,
        _ => null,
    };

    public static EasingCurve? FromValue(object value) => value switch
    {
        string name => FromName(name),
        TomlArray { Count: 4 } points when Number(points[0]) is { } x1 && Number(points[1]) is { } y1 &&
            Number(points[2]) is { } x2 && Number(points[3]) is { } y2 && double.IsFinite(x1 + y1 + x2 + y2) =>
            EasingCurve.CubicBezier(Math.Clamp(x1, 0.0, 1.0), y1, Math.Clamp(x2, 0.0, 1.0), y2),
        _ => null,
    };

    public static string NameOf(EasingCurve curve) => curve.Kind switch
    {
        EasingKind.Linear => "linear",
        EasingKind.Sigmoid => "sigmoid",
        EasingKind.Circle => "circle",
        EasingKind.InCurve => "in-curve",
        EasingKind.OutCurve => "out-curve",
        EasingKind.InCubic => "in-cubic",
        EasingKind.InOutCubic => "in-out-cubic",
        EasingKind.InOutSine => "in-out-sine",
        EasingKind.CubicBezier => string.Create(
            CultureInfo.InvariantCulture, $"[{curve.X1}, {curve.Y1}, {curve.X2}, {curve.Y2}]"),
        _ => "out-cubic",
    };

    private static double? Number(object? value) => value switch
    {
        double fraction => fraction,
        long whole => whole,
        _ => null,
    };
}
