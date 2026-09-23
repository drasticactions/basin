using Basin.Capabilities;
using Pixman;

namespace Basin.Frames.Quill;

public sealed class QuillFrameGeometry
{
    private readonly Box[] _buttons = new Box[6];

    public int Width { get; internal set; }

    public int Height { get; internal set; }

    public int Border { get; internal set; }

    public int TitleHeight { get; internal set; }

    public Box Title { get; internal set; }

    public Box Icon { get; internal set; }

    public Box Menu { get; internal set; }

    public Box Close
    {
        get => _buttons[0];
        internal set => _buttons[0] = value;
    }

    public Box Maximize
    {
        get => _buttons[1];
        internal set => _buttons[1] = value;
    }

    public Box Minimize
    {
        get => _buttons[2];
        internal set => _buttons[2] = value;
    }

    public Box Shade
    {
        get => _buttons[3];
        internal set => _buttons[3] = value;
    }

    public Box Above
    {
        get => _buttons[4];
        internal set => _buttons[4] = value;
    }

    public Box Stick
    {
        get => _buttons[5];
        internal set => _buttons[5] = value;
    }

    public bool HasRoundedCorner { get; internal set; }

    public Box BoundsOf(FramePart part) => part switch
    {
        FramePart.Title => Title,
        FramePart.Icon => Icon,
        FramePart.Menu => Menu,
        FramePart.Close => Close,
        FramePart.Maximize => Maximize,
        FramePart.Minimize => Minimize,
        FramePart.Shade => Shade,
        FramePart.Above => Above,
        FramePart.Stick => Stick,
        _ => default,
    };

    internal void Reset()
    {
        Title = default;
        Icon = default;
        Menu = default;
        for (var i = 0; i < _buttons.Length; i++)
        {
            _buttons[i] = default;
        }
    }
}
