using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

/// <summary>
/// A node that fills a rectangle with a solid color.
/// </summary>
public sealed class SceneRect : SceneNode
{
    private int _width;
    private int _height;
    private RenderColor _color;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneRect"/> class.
    /// </summary>
    /// <param name="parent">The tree to add the rectangle to.</param>
    /// <param name="width">The width, in logical pixels.</param>
    /// <param name="height">The height, in logical pixels.</param>
    /// <param name="color">The premultiplied fill color.</param>
    public SceneRect(SceneTree parent, int width, int height, RenderColor color)
        : base(parent)
    {
        _width = width;
        _height = height;
        _color = color;
        DamageSubtree();
    }

    /// <summary>
    /// Gets or sets the width, in logical pixels.
    /// </summary>
    public int Width
    {
        get => _width;
        set => Resize(ref _width, value);
    }

    /// <summary>
    /// Gets or sets the height, in logical pixels.
    /// </summary>
    public int Height
    {
        get => _height;
        set => Resize(ref _height, value);
    }

    /// <summary>
    /// Gets or sets the premultiplied fill color.
    /// </summary>
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

    /// <summary>
    /// Gets a value indicating whether the color alpha is 1 or more.
    /// </summary>
    public bool IsOpaque => _color.A >= 1f;

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Scene.SurfaceAt"/> stops at this rectangle instead of reaching surfaces below it.
    /// </summary>
    public bool BlocksInput { get; set; }

    /// <inheritdoc/>
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
