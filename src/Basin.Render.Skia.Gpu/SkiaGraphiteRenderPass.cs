using Basin.Diagnostics;
using Pixman;
using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed class SkiaGraphiteRenderPass : IRenderPass
{
    private readonly SkiaGraphiteRenderer _renderer;
    private readonly SKPaint _paint;
    private IBuffer? _target;
    private SkiaGraphiteTarget? _entry;
    private int _signalFenceFd = -1;

    internal SkiaGraphiteRenderPass(SkiaGraphiteRenderer renderer, SKPaint paint)
    {
        _renderer = renderer;
        _paint = paint;
    }

    internal void Begin(IBuffer target, SkiaGraphiteTarget entry, int signalFenceFd)
    {
        if (_target is not null)
        {
            throw new InvalidOperationException("The previous render pass was not submitted.");
        }

        _target = target;
        _entry = entry;
        _signalFenceFd = signalFenceFd;
    }

    public void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Rect(_entry!.Canvas, _paint, color, box, clip);
    }

    public void AddTexture(ITexture texture, in TextureRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Texture(_entry!.Canvas, _paint, (ISkiaTexture)texture, options);
    }

    public void AddMesh(ITexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Mesh(_entry!.Canvas, _paint, (ISkiaTexture?)texture, vertices, options);
    }

    public void AddShader(IPixelShader shader, in ShaderRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Shader(_entry!.Canvas, _paint, shader, options);
    }

    private int _scopedSubmits;

    public bool Submit()
    {
        if (_scopedSubmits < 30)
        {
            _scopedSubmits++;
            return SubmitCore();
        }

        AllocationScope.Begin(region: "SkiaGraphiteSubmit", forgiving: true);
        try
        {
            return SubmitCore();
        }
        finally
        {
            AllocationScope.End();
        }
    }

    private bool SubmitCore()
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        var target = _target;
        var entry = _entry!;
        _target = null;
        _entry = null;

        if (_renderer.ForeignThisFrame.Count > 0)
        {
            _renderer.Device.SubmitImmediate(_renderer, static (renderer, commands) =>
            {
                foreach (var image in renderer.ForeignThisFrame)
                {
                    image.RecordForeignAcquire(commands);
                }
            });
        }

        var recording = GraphiteNative.RecorderSnap(_renderer.Recorder.Handle);
        var inserted = SKGraphiteInsertStatus.Success;
        if (recording != 0)
        {
            inserted = _renderer.Context.InsertRecording(new SKGraphiteInsertRecordingInfo { Recording = recording });
            _ = _renderer.Context.Submit(new SKGraphiteSubmitInfo());
            GraphiteNative.RecordingDelete(recording);
        }

        _renderer.Sync.DrainAndSignal(_signalFenceFd);
        _signalFenceFd = -1;

        if (_renderer.ForeignThisFrame.Count > 0)
        {
            _renderer.Device.SubmitImmediate(_renderer, static (renderer, commands) =>
            {
                foreach (var image in renderer.ForeignThisFrame)
                {
                    image.RecordForeignRelease(commands);
                }
            });
            _renderer.ForeignThisFrame.Clear();
        }

        if (entry.IsCpuReadback)
        {
            entry.ReadInto(target);
        }

        return recording != 0 && inserted == SKGraphiteInsertStatus.Success;
    }
}
