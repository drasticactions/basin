using Basin;
using Basin.Capabilities;

namespace PlasmaHost;

internal readonly record struct BreezeButtons
{
    public Box Menu { get; init; }

    public Box Minimize { get; init; }

    public Box Maximize { get; init; }

    public Box Close { get; init; }

    public int TitleLeft { get; init; }

    public int TitleRight { get; init; }

    public BreezeButtons With(FramePart part, in Box box) => part switch
    {
        FramePart.Menu => this with { Menu = box },
        FramePart.Minimize => this with { Minimize = box },
        FramePart.Maximize => this with { Maximize = box },
        FramePart.Close => this with { Close = box },
        _ => this,
    };

    public Box BoundsOf(FramePart part) => part switch
    {
        FramePart.Menu => Menu,
        FramePart.Minimize => Minimize,
        FramePart.Maximize => Maximize,
        FramePart.Close => Close,
        _ => default,
    };

    public FramePart PartAt(double x, double y)
    {
        if (Hits(Close, x, y))
        {
            return FramePart.Close;
        }

        if (Hits(Maximize, x, y))
        {
            return FramePart.Maximize;
        }

        if (Hits(Minimize, x, y))
        {
            return FramePart.Minimize;
        }

        return Hits(Menu, x, y) ? FramePart.Menu : FramePart.None;
    }

    private static bool Hits(in Box box, double x, double y) =>
        !box.IsEmpty && x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom;
}
