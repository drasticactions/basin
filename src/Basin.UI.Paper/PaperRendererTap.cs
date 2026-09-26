using System.Runtime.InteropServices;
using Prowl.Quill;
using Prowl.Vector;

namespace Basin.UI.Paper;

internal sealed class PaperRendererTap : ICanvasRenderer
{
    private readonly ICanvasRenderer _inner;
    private HashCode _hash;

    internal PaperRendererTap(ICanvasRenderer inner)
    {
        _inner = inner;
    }

    public bool SupportsBackdropBlur => _inner.SupportsBackdropBlur;

    internal int Fingerprint { get; private set; }

    internal int Frames { get; private set; }

    internal int DrawCalls { get; private set; }

    public object CreateTexture(uint width, uint height) => _inner.CreateTexture(width, height);

    public Int2 GetTextureSize(object texture) => _inner.GetTextureSize(texture);

    public void SetTextureData(object texture, IntRect bounds, byte[] data)
    {
        _inner.SetTextureData(texture, bounds, data);
        _hash.AddBytes(MemoryMarshal.AsBytes([bounds]));
    }

    public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
    {
        if (canvas is not null && drawCalls is not null)
        {
            _hash.AddBytes(MemoryMarshal.AsBytes(canvas.VertexBuffer.AsSpan(0, canvas.VertexCount)));
            _hash.AddBytes(MemoryMarshal.AsBytes(canvas.IndexBuffer.AsSpan(0, canvas.IndexCount)));
            for (var i = 0; i < drawCalls.Count; i++)
            {
                var call = drawCalls[i];
                _hash.Add(call.ElementCount);
                _hash.Add((int)call.Brush.Type);
                Add(in call.Brush.Color1);
                Add(in call.Brush.Color2);
                Add(in call.Brush.Point1);
                Add(in call.Brush.Point2);
                Add(in call.Brush.Transform);
                _hash.Add(call.Brush.CornerRadii);
                _hash.Add(call.Brush.Feather);
            }

            DrawCalls = drawCalls.Count;
        }

        Fingerprint = _hash.ToHashCode();
        _hash = default;
        Frames++;
        _inner.RenderCalls(canvas!, drawCalls!);
    }

    public void Dispose()
    {
    }

    private void Add<T>(in T value)
        where T : unmanaged => _hash.AddBytes(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));
}
