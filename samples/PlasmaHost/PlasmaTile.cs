using Basin;

namespace PlasmaHost;

internal sealed class PlasmaTile
{
    public PlasmaTile(bool horizontal) => Horizontal = horizontal;

    public bool Horizontal { get; set; }

    public double Fraction { get; set; } = 1.0;

    public List<PlasmaTile> Children { get; } = [];

    public Box Box { get; set; }

    public bool IsLeaf => Children.Count == 0;

    public void Place(in Box area)
    {
        Box = area;
        if (Children.Count == 0)
        {
            return;
        }

        var offset = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var last = i == Children.Count - 1;
            if (Horizontal)
            {
                var width = last ? area.Width - offset : (int)Math.Round(area.Width * child.Fraction);
                child.Place(new Box(area.X + offset, area.Y, Math.Max(1, width), area.Height));
                offset += width;
            }
            else
            {
                var height = last ? area.Height - offset : (int)Math.Round(area.Height * child.Fraction);
                child.Place(new Box(area.X, area.Y + offset, area.Width, Math.Max(1, height)));
                offset += height;
            }
        }
    }

    public void Leaves(List<PlasmaTile> into)
    {
        if (Children.Count == 0)
        {
            into.Add(this);
            return;
        }

        foreach (var child in Children)
        {
            child.Leaves(into);
        }
    }

    public void Split(bool horizontal)
    {
        if (Children.Count != 0)
        {
            return;
        }

        Horizontal = horizontal;
        Children.Add(new PlasmaTile(!horizontal) { Fraction = 0.5 });
        Children.Add(new PlasmaTile(!horizontal) { Fraction = 0.5 });
    }

    public static PlasmaTile Default()
    {
        var root = new PlasmaTile(horizontal: true);
        root.Children.Add(new PlasmaTile(horizontal: false) { Fraction = 0.5 });
        root.Children.Add(new PlasmaTile(horizontal: false) { Fraction = 0.5 });
        return root;
    }
}
