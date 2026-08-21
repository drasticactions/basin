using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

public sealed class SceneRect : SceneNode
{
    private int _width;
    private int _height;
    private RenderColor _color;

    public SceneRect(SceneTree parent, int width, int height, RenderColor color)
        : base(parent)
    {
        _width = width;
        _height = height;
        _color = color;
        DamageSubtree();
    }

    public int Width
    {
        get => _width;
        set => Resize(ref _width, value);
    }

    public int Height
    {
        get => _height;
        set => Resize(ref _height, value);
    }

    public RenderColor Color
    {
        get => _color;
        set
        {
            if (_color != value)
            {
                _color = value;
                DamageSubtree();
            }
        }
    }

    public bool IsOpaque => _color.A >= 1f;

    protected override (int Width, int Height) ContentSize => (_width, _height);

    private void Resize(ref int field, int value)
    {
        if (field != value)
        {
            DamageSubtree();
            field = value;
            DamageSubtree();
        }
    }
}
