using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

public static class LayerLayout
{
    public readonly record struct LayerSpec(
        LayerKind Layer,
        LayerAnchor Anchor,
        int ExclusiveZone,
        (int Top, int Right, int Bottom, int Left) Margin,
        int DesiredWidth,
        int DesiredHeight,
        LayerAnchor ExclusiveEdge = LayerAnchor.None);

    public readonly record struct Placement(int Index, Box Box);

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

    public static (List<Placement> Placements, Box UsableArea) Arrange(Box outputBox, ReadOnlySpan<LayerSpec> specs)
    {
        var placements = new List<Placement>(specs.Length);
        var usable = outputBox with { X = 0, Y = 0 };

        foreach (var exclusive in new[] { true, false })
        {
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
