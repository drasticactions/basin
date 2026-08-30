using Basin;
using Basin.Scene;

namespace TinyComp;

internal sealed class FrameCornerRig : IDisposable
{
    private readonly IRenderer _renderer;
    private readonly Frame _frame;
    private readonly SceneBuffer _content;
    private readonly int _radius;
    private readonly IPixelShader?[] _handles = new IPixelShader?[5];
    private Box _lastOuter;
    private double _lastScale = -1;
    private bool _disposed;

    public FrameCornerRig(IRenderer renderer, Frame frame, SceneBuffer content, int radius)
    {
        _renderer = renderer;
        _frame = frame;
        _content = content;
        _radius = radius;
        _frame.Committed += Update;
        Update();
    }

    private void Update()
    {
        if (_disposed || _frame.IsFaulted)
        {
            return;
        }

        var outer = _frame.OuterBounds;
        if (outer.IsEmpty)
        {
            return;
        }

        var scale = _frame.Scale;
        if (outer == _lastOuter && scale == _lastScale)
        {
            return;
        }

        var insets = _frame.Insets;
        Span<Box> boxes = stackalloc Box[5];
        for (var i = 0; i < 4; i++)
        {
            boxes[i] = _frame.StripBounds(i);
        }

        boxes[4] = new Box(
            insets.Left,
            insets.Top,
            outer.Width - insets.Left - insets.Right,
            outer.Height - insets.Top - insets.Bottom);

        for (var i = 0; i < 5; i++)
        {
            if (_handles[i] is null)
            {
                var compiled = _renderer.CompilePixelShader(CornerShader.OuterSource, CornerShader.OuterUniforms);
                if (compiled is null)
                {
                    return;
                }

                _handles[i] = compiled;
            }

            _handles[i]!.SetUniforms(
            [
                (float)(_radius * scale),
                ((float)(outer.Width * scale), (float)(outer.Height * scale)),
                ((float)(boxes[i].X * scale), (float)(boxes[i].Y * scale)),
            ]);
        }

        var strips = _frame.StripNodes;
        for (var i = 0; i < 4; i++)
        {
            strips[i].TextureShader = _handles[i];
        }

        _content.TextureShader = _handles[4];
        _lastOuter = outer;
        _lastScale = scale;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _frame.Committed -= Update;
        var strips = _frame.StripNodes;
        for (var i = 0; i < 4; i++)
        {
            if (!strips[i].IsDestroyed)
            {
                strips[i].TextureShader = null;
            }
        }

        if (!_content.IsDestroyed)
        {
            _content.TextureShader = null;
        }

        foreach (var handle in _handles)
        {
            handle?.Dispose();
        }
    }
}
