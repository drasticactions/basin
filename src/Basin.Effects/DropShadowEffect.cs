using Basin.Scene;

namespace Basin.Effects;

public sealed class DropShadowEffect : IDisposable
{
    private const int MinimumSize = 5;

    private readonly SceneTree _tree;
    private readonly SceneBuffer[] _cells = new SceneBuffer[8];

    private DropShadowTexture? _texture;
    private Box _geometry;
    private bool _visible = true;
    private bool _disposed;

    public DropShadowEffect(SceneTree parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _tree = new SceneTree(parent) { Enabled = false };
        _tree.LowerToBottom();
        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new SceneBuffer(_tree) { InputEnabled = false, Enabled = false };
        }
    }

    public SceneTree Tree => _tree;

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

    public Box Geometry => _geometry;

    public void SetGeometry(in Box outer)
    {
        if (_geometry == outer)
        {
            return;
        }

        _geometry = outer;
        Layout();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _texture = null;
        foreach (var cell in _cells)
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

        if (!_visible || _texture is not { } texture ||
            _geometry.Width < MinimumSize || _geometry.Height < MinimumSize)
        {
            Hide();
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

        _tree.Enabled = true;
        SetCell(0, top, 0, -1);
        SetCell(1, topRight, 1, -1);
        SetCell(2, right, 1, 0);
        SetCell(3, bottomRight, 1, 1);
        SetCell(4, bottom, 0, 1);
        SetCell(5, bottomLeft, -1, 1);
        SetCell(6, left, -1, 0);
        SetCell(7, topLeft, -1, -1);
    }

    private void Hide()
    {
        _tree.Enabled = false;
        foreach (var cell in _cells)
        {
            cell.Enabled = false;
            cell.SetBuffer(null);
        }
    }

    private void SetCell(int index, in Quad quad, int anchorX, int anchorY)
    {
        var cell = _cells[index];
        var texture = _texture!;
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
