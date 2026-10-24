using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Stretches a nine-patch <see cref="DropShadowTexture"/> around a window box with eight buffer nodes. The consumer owns the textures and decides which windows get a shadow.
/// </summary>
public sealed class DropShadowEffect : IDisposable
{
    private const int MinimumSize = 5;

    private readonly SceneTree _tree;
    private readonly SceneBuffer[] _cells = new SceneBuffer[8];
    private readonly SceneTree _glow;
    private readonly SceneBuffer[] _glowCells = new SceneBuffer[8];

    private DropShadowTexture? _texture;
    private DropShadowTexture? _glowTexture;
    private double _glowOpacity;
    private Box _geometry;
    private bool _visible = true;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DropShadowEffect"/> class. Its tree goes to the bottom of the parent and stays disabled until it has a texture and a geometry.
    /// </summary>
    /// <param name="parent">The window tree that the shadow draws under.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <see langword="null"/>.</exception>
    public DropShadowEffect(SceneTree parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _tree = new SceneTree(parent) { Enabled = false, DebugName = "shadow" };
        _tree.LowerToBottom();
        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new SceneBuffer(_tree) { InputEnabled = false, Enabled = false };
        }

        _glow = new SceneTree(_tree) { Enabled = false, DebugName = "shadow-glow" };
        for (var i = 0; i < _glowCells.Length; i++)
        {
            _glowCells[i] = new SceneBuffer(_glow) { InputEnabled = false, Enabled = false };
        }
    }

    /// <summary>
    /// Gets the tree that holds the shadow cells and the glow tree.
    /// </summary>
    public SceneTree Tree => _tree;

    /// <summary>
    /// Gets or sets the shadow texture. The effect does not own it, so the caller must keep it alive while it is set.
    /// </summary>
    public DropShadowTexture? Texture
    {
        get => _texture;
        set
        {
            if (ReferenceEquals(_texture, value))
            {
                return;
            }

            _texture = value;
            Layout();
        }
    }

    /// <summary>
    /// Gets or sets the glow texture that draws above the shadow at <see cref="GlowOpacity"/>. The effect does not own it.
    /// </summary>
    public DropShadowTexture? GlowTexture
    {
        get => _glowTexture;
        set
        {
            if (ReferenceEquals(_glowTexture, value))
            {
                return;
            }

            _glowTexture = value;
            Layout();
        }
    }

    /// <summary>
    /// Gets or sets the alpha of the glow, from 0 to 1. At 0 the glow tree is disabled. The setter clamps values outside the range.
    /// </summary>
    public double GlowOpacity
    {
        get => _glowOpacity;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (_glowOpacity == clamped)
            {
                return;
            }

            var wasShown = _glowOpacity > 0;
            _glowOpacity = clamped;
            _glow.Alpha = (float)clamped;
            if (wasShown != clamped > 0)
            {
                Layout();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the consumer wants the shadow shown. It is <see langword="true"/> by default.
    /// </summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value)
            {
                return;
            }

            _visible = value;
            Layout();
        }
    }

    /// <summary>
    /// Gets the window box that the shadow surrounds, in the coordinates of the parent tree.
    /// </summary>
    public Box Geometry => _geometry;

    /// <summary>
    /// Sets the window box and lays out the cells again. A box smaller than 5 pixels on either side hides the shadow.
    /// </summary>
    /// <param name="outer">The window box in the coordinates of the parent tree.</param>
    public void SetGeometry(in Box outer)
    {
        if (_geometry == outer)
        {
            return;
        }

        _geometry = outer;
        Layout();
    }

    /// <summary>
    /// Detaches the textures and destroys the tree. It does not dispose the textures. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _texture = null;
        _glowTexture = null;
        foreach (var cell in _cells)
        {
            cell.SetBuffer(null);
        }

        foreach (var cell in _glowCells)
        {
            cell.SetBuffer(null);
        }

        if (!_tree.IsDestroyed)
        {
            _tree.Destroy();
        }
    }

    private void Layout()
    {
        if (_disposed)
        {
            return;
        }

        if (!_visible || _geometry.Width < MinimumSize || _geometry.Height < MinimumSize ||
            (_texture is null && (_glowTexture is null || _glowOpacity <= 0)))
        {
            Hide();
            return;
        }

        _tree.Enabled = true;
        LayoutCells(_cells, _texture);
        var glowing = _glowTexture is not null && _glowOpacity > 0;
        _glow.Enabled = glowing;
        LayoutCells(_glowCells, glowing ? _glowTexture : null);
    }

    private void LayoutCells(SceneBuffer[] cells, DropShadowTexture? texture)
    {
        if (texture is null)
        {
            HideCells(cells);
            return;
        }

        var scale = texture.Scale;
        var outer = new Quad
        {
            Left = _geometry.X - texture.PaddingLeft,
            Top = _geometry.Y - texture.PaddingTop,
            Right = _geometry.Right + texture.PaddingRight,
            Bottom = _geometry.Bottom + texture.PaddingBottom,
        };

        var leadWidth = texture.Center.X / scale;
        var leadHeight = texture.Center.Y / scale;
        var trailWidth = (texture.Width - texture.Center.Right) / scale;
        var trailHeight = (texture.Height - texture.Center.Bottom) / scale;

        var topLeft = Quad.At(outer.Left, outer.Top, leadWidth, leadHeight);
        var topRight = Quad.At(outer.Right - trailWidth, outer.Top, trailWidth, leadHeight);
        var bottomLeft = Quad.At(outer.Left, outer.Bottom - trailHeight, leadWidth, trailHeight);
        var bottomRight = Quad.At(outer.Right - trailWidth, outer.Bottom - trailHeight, trailWidth, trailHeight);

        DistributeHorizontally(ref topLeft, ref topRight);
        DistributeHorizontally(ref bottomLeft, ref bottomRight);
        DistributeVertically(ref topLeft, ref bottomLeft);
        DistributeVertically(ref topRight, ref bottomRight);

        var top = new Quad
        {
            Left = topLeft.Right,
            Top = outer.Top,
            Right = topRight.Left,
            Bottom = outer.Top + leadHeight,
        };
        var bottom = new Quad
        {
            Left = bottomLeft.Right,
            Top = outer.Bottom - trailHeight,
            Right = bottomRight.Left,
            Bottom = outer.Bottom,
        };
        var left = new Quad
        {
            Left = outer.Left,
            Top = topLeft.Bottom,
            Right = outer.Left + leadWidth,
            Bottom = bottomLeft.Top,
        };
        var right = new Quad
        {
            Left = outer.Right - trailWidth,
            Top = topRight.Bottom,
            Right = outer.Right,
            Bottom = bottomRight.Top,
        };

        DistributeHorizontally(ref left, ref right);
        DistributeVertically(ref top, ref bottom);

        SetCell(cells, texture, 0, top, 0, -1);
        SetCell(cells, texture, 1, topRight, 1, -1);
        SetCell(cells, texture, 2, right, 1, 0);
        SetCell(cells, texture, 3, bottomRight, 1, 1);
        SetCell(cells, texture, 4, bottom, 0, 1);
        SetCell(cells, texture, 5, bottomLeft, -1, 1);
        SetCell(cells, texture, 6, left, -1, 0);
        SetCell(cells, texture, 7, topLeft, -1, -1);
    }

    private void Hide()
    {
        _tree.Enabled = false;
        _glow.Enabled = false;
        HideCells(_cells);
        HideCells(_glowCells);
    }

    private static void HideCells(SceneBuffer[] cells)
    {
        foreach (var cell in cells)
        {
            cell.Enabled = false;
            cell.SetBuffer(null);
        }
    }

    private static void SetCell(SceneBuffer[] cells, DropShadowTexture texture, int index, in Quad quad, int anchorX, int anchorY)
    {
        var cell = cells[index];
        var x = (int)Math.Round(quad.Left, MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(quad.Top, MidpointRounding.AwayFromZero);
        var width = (int)Math.Round(quad.Right, MidpointRounding.AwayFromZero) - x;
        var height = (int)Math.Round(quad.Bottom, MidpointRounding.AwayFromZero) - y;
        if (width <= 0 || height <= 0)
        {
            cell.Enabled = false;
            cell.SetBuffer(null);
            return;
        }

        var sourceWidth = anchorX == 0
            ? texture.Center.Width
            : Math.Min(width * texture.Scale, texture.Width);
        var sourceHeight = anchorY == 0
            ? texture.Center.Height
            : Math.Min(height * texture.Scale, texture.Height);
        var sourceX = anchorX switch
        {
            0 => texture.Center.X,
            > 0 => texture.Width - sourceWidth,
            _ => 0,
        };
        var sourceY = anchorY switch
        {
            0 => texture.Center.Y,
            > 0 => texture.Height - sourceHeight,
            _ => 0,
        };

        cell.SetBuffer(texture.Buffer);
        cell.SourceBox = new FBox(sourceX, sourceY, sourceWidth, sourceHeight);
        cell.DestinationWidth = width;
        cell.DestinationHeight = height;
        cell.SetPosition(x, y);
        cell.Enabled = true;
    }

    private static void DistributeHorizontally(ref Quad first, ref Quad second)
    {
        if (first.Right <= second.Left)
        {
            return;
        }

        var boundedRight = Math.Min(first.Right, second.Right);
        var boundedLeft = Math.Max(first.Left, second.Left);
        var half = (boundedRight - boundedLeft) / 2;
        first.Right = boundedRight - half;
        second.Left = boundedLeft + half;
    }

    private static void DistributeVertically(ref Quad first, ref Quad second)
    {
        if (first.Bottom <= second.Top)
        {
            return;
        }

        var boundedBottom = Math.Min(first.Bottom, second.Bottom);
        var boundedTop = Math.Max(first.Top, second.Top);
        var half = (boundedBottom - boundedTop) / 2;
        first.Bottom = boundedBottom - half;
        second.Top = boundedTop + half;
    }

    private struct Quad
    {
        public double Left;
        public double Top;
        public double Right;
        public double Bottom;

        public static Quad At(double x, double y, double width, double height) => new()
        {
            Left = x,
            Top = y,
            Right = x + width,
            Bottom = y + height,
        };
    }
}
