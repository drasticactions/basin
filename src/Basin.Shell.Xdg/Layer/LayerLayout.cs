using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The layer-shell arrangement math for one output. It places surfaces by anchors, margins and exclusive zones in output-local logical pixels, and has no state.
/// </summary>
public static class LayerLayout
{
    /// <summary>
    /// The layer state of one surface, as <see cref="Arrange(Box, ReadOnlySpan{LayerSpec})"/> reads it.
    /// </summary>
    /// <param name="Layer">The stacking layer.</param>
    /// <param name="Anchor">The anchored edges.</param>
    /// <param name="ExclusiveZone">The exclusive zone in logical pixels. A positive value reserves space, 0 avoids reserved space, and a negative value ignores it.</param>
    /// <param name="Margin">The margins in logical pixels.</param>
    /// <param name="DesiredWidth">The requested width in logical pixels. A value of 0 fills the bounds between the margins.</param>
    /// <param name="DesiredHeight">The requested height in logical pixels. A value of 0 fills the bounds between the margins.</param>
    /// <param name="ExclusiveEdge">The edge that the exclusive zone belongs to, or <see cref="LayerAnchor.None"/> to deduce it from the anchors.</param>
    public readonly record struct LayerSpec(
        LayerKind Layer,
        LayerAnchor Anchor,
        int ExclusiveZone,
        (int Top, int Right, int Bottom, int Left) Margin,
        int DesiredWidth,
        int DesiredHeight,
        LayerAnchor ExclusiveEdge = LayerAnchor.None);

    /// <summary>
    /// The box that the arrangement gives one surface.
    /// </summary>
    /// <param name="Index">The index of the surface in the input list.</param>
    /// <param name="Box">The box in output-local logical pixels.</param>
    public readonly record struct Placement(int Index, Box Box);

    /// <summary>
    /// Arranges live layer surfaces from the state of their last valid commit. Mapped and unmapped surfaces are both included.
    /// </summary>
    /// <param name="outputBox">The output box. Only its size is used.</param>
    /// <param name="surfaces">The layer surfaces on the output.</param>
    /// <returns>A placement for each surface, and the area that the exclusive zones leave, both in output-local logical pixels.</returns>
    public static (List<Placement> Placements, Box UsableArea) Arrange(Box outputBox, IReadOnlyList<LayerSurface> surfaces)
    {
        var specs = new LayerSpec[surfaces.Count];
        for (var i = 0; i < surfaces.Count; i++)
        {
            var s = surfaces[i];
            specs[i] = new LayerSpec(
                s.Layer, s.Anchor, s.ExclusiveZone, s.Margin, s.DesiredWidth, s.DesiredHeight, s.ExclusiveEdge);
        }

        return Arrange(outputBox, specs);
    }

    /// <summary>
    /// Arranges surfaces in two passes. Surfaces with a positive exclusive zone go first and reserve their strips, from the overlay layer down. The other surfaces go next in the area that remains.
    /// </summary>
    /// <param name="outputBox">The output box. Only its size is used.</param>
    /// <param name="specs">The layer state of each surface.</param>
    /// <returns>A placement for each surface in arrangement order, and the area that the exclusive zones leave, both in output-local logical pixels.</returns>
    public static (List<Placement> Placements, Box UsableArea) Arrange(Box outputBox, ReadOnlySpan<LayerSpec> specs)
    {
        var placements = new List<Placement>(specs.Length);
        var usable = outputBox with { X = 0, Y = 0 };

        for (var pass = 0; pass < 2; pass++)
        {
            var exclusive = pass == 0;
            for (var layer = (int)LayerKind.Overlay; layer >= (int)LayerKind.Background; layer--)
            {
                for (var i = 0; i < specs.Length; i++)
                {
                    var spec = specs[i];
                    if ((int)spec.Layer != layer || (spec.ExclusiveZone > 0) != exclusive)
                    {
                        continue;
                    }

                    var box = PlaceOne(spec, outputBox, usable, out var claimed);
                    placements.Add(new Placement(i, box));
                    if (exclusive && !claimed.IsEmpty)
                    {
                        usable = Subtract(usable, claimed);
                    }
                }
            }
        }

        return (placements, usable);
    }

    private static Box PlaceOne(in LayerSpec surface, Box outputBox, Box usable, out Box claimed)
    {
        claimed = default;
        var anchor = surface.Anchor;
        var (marginTop, marginRight, marginBottom, marginLeft) = surface.Margin;

        var bounds = surface.ExclusiveZone < 0 ? outputBox with { X = 0, Y = 0 } : usable;

        var width = surface.DesiredWidth;
        var height = surface.DesiredHeight;
        if (width == 0)
        {
            width = Math.Max(0, bounds.Width - marginLeft - marginRight);
        }

        if (height == 0)
        {
            height = Math.Max(0, bounds.Height - marginTop - marginBottom);
        }

        var anchorsHorizontal = anchor & (LayerAnchor.Left | LayerAnchor.Right);
        var x = anchorsHorizontal switch
        {
            LayerAnchor.Left => bounds.X + marginLeft,
            LayerAnchor.Right => bounds.Right - width - marginRight,
            LayerAnchor.Left | LayerAnchor.Right => bounds.X + marginLeft,
            _ => bounds.X + (bounds.Width - width) / 2,
        };

        var anchorsVertical = anchor & (LayerAnchor.Top | LayerAnchor.Bottom);
        var y = anchorsVertical switch
        {
            LayerAnchor.Top => bounds.Y + marginTop,
            LayerAnchor.Bottom => bounds.Bottom - height - marginBottom,
            LayerAnchor.Top | LayerAnchor.Bottom => bounds.Y + marginTop,
            _ => bounds.Y + (bounds.Height - height) / 2,
        };

        var box = new Box(x, y, width, height);

        if (surface.ExclusiveZone > 0)
        {
            var zone = surface.ExclusiveZone;
            if (surface.ExclusiveEdge != LayerAnchor.None)
            {
                claimed = Claim(surface.ExclusiveEdge, outputBox, zone, surface.Margin);
            }
            else
            {
                var fullWidth = anchorsHorizontal is LayerAnchor.None or (LayerAnchor.Left | LayerAnchor.Right);
                var fullHeight = anchorsVertical is LayerAnchor.None or (LayerAnchor.Top | LayerAnchor.Bottom);
                var edge = (anchorsVertical, anchorsHorizontal) switch
                {
                    (LayerAnchor.Top, _) when fullWidth => LayerAnchor.Top,
                    (LayerAnchor.Bottom, _) when fullWidth => LayerAnchor.Bottom,
                    (_, LayerAnchor.Left) when fullHeight => LayerAnchor.Left,
                    (_, LayerAnchor.Right) when fullHeight => LayerAnchor.Right,
                    _ => LayerAnchor.None,
                };

                claimed = Claim(edge, outputBox, zone, surface.Margin);
            }
        }

        return box;
    }

    private static Box Claim(LayerAnchor edge, Box outputBox, int zone, (int Top, int Right, int Bottom, int Left) margin) =>
        edge switch
        {
            LayerAnchor.Top => new Box(0, 0, outputBox.Width, zone + margin.Top),
            LayerAnchor.Bottom => new Box(0, outputBox.Height - zone - margin.Bottom, outputBox.Width, zone + margin.Bottom),
            LayerAnchor.Left => new Box(0, 0, zone + margin.Left, outputBox.Height),
            LayerAnchor.Right => new Box(outputBox.Width - zone - margin.Right, 0, zone + margin.Right, outputBox.Height),
            _ => default,
        };

    private static Box Subtract(Box area, Box claim)
    {
        if (claim.Width >= area.Width && claim.Y <= area.Y)
        {
            var taken = claim.Bottom - area.Y;
            return taken > 0 ? new Box(area.X, area.Y + taken, area.Width, Math.Max(0, area.Height - taken)) : area;
        }

        if (claim.Width >= area.Width)
        {
            var taken = area.Bottom - claim.Y;
            return taken > 0 ? area with { Height = Math.Max(0, area.Height - taken) } : area;
        }

        if (claim.Height >= area.Height && claim.X <= area.X)
        {
            var taken = claim.Right - area.X;
            return taken > 0 ? new Box(area.X + taken, area.Y, Math.Max(0, area.Width - taken), area.Height) : area;
        }

        if (claim.Height >= area.Height)
        {
            var taken = area.Right - claim.X;
            return taken > 0 ? area with { Width = Math.Max(0, area.Width - taken) } : area;
        }

        return area;
    }
}
