using Pixman;

namespace Basin.Render.Pixman;

public sealed class PixmanRenderer : IRenderer
{
    private readonly Dictionary<IBuffer, TargetEntry> _targets = [];
    private readonly PixmanRenderPass _pass = new();

    public ITexture? ImportTexture(IBuffer buffer) => new PixmanTexture(buffer);

    public static RenderStack CreateStack() => new(new PixmanRenderer(), null);

    public ColorTransformCapability ColorTransform => ColorTransformCapability.Lut3D;

    public RenderFencePrecision FencePrecision => RenderFencePrecision.None;

    public IColorLut? ImportLut(ColorLut3D lut) => new PixmanColorLut(lut);

    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        RenderFences.WaitSyncFile(options.WaitFenceFd);
        if (!target.BeginDataAccess(BufferDataAccess.Read | BufferDataAccess.Write, out var view))
        {
            throw new InvalidOperationException("Render target has no CPU-accessible pixels.");
        }

        if (!_targets.TryGetValue(target, out var entry) || entry.Data != view.Data)
        {
            entry = ImportTarget(target, view);
        }

        _pass.Begin(target, entry.Image, view.Data, view.Stride);
        return _pass;
    }

    private TargetEntry ImportTarget(IBuffer target, in BufferDataView view)
    {
        if (_targets.Remove(target, out var stale))
        {
            stale.Image.Dispose();
        }

        var entry = new TargetEntry(
            view.Data,
            PixmanImage.CreateBits(ToPixmanFormat(view.Format), target.Width, target.Height, view.Data, view.Stride));
        _targets[target] = entry;
        target.Destroyed += () =>
        {
            if (_targets.Remove(target, out var dead))
            {
                dead.Image.Dispose();
            }
        };
        return entry;
    }

    public void Dispose()
    {
        foreach (var entry in _targets.Values)
        {
            entry.Image.Dispose();
        }

        _targets.Clear();
        _pass.DisposeScratch();
    }

    internal static PixmanFormat ToPixmanFormat(DrmFormat format) => format switch
    {
        DrmFormat.Argb8888 => PixmanFormat.A8R8G8B8,
        DrmFormat.Xrgb8888 => PixmanFormat.X8R8G8B8,
        DrmFormat.Abgr8888 => PixmanFormat.A8B8G8R8,
        DrmFormat.Xbgr8888 => PixmanFormat.X8B8G8R8,
        DrmFormat.Argb2101010 => PixmanFormat.A2R10G10B10,
        DrmFormat.Xrgb2101010 => PixmanFormat.X2R10G10B10,
        DrmFormat.Abgr2101010 => PixmanFormat.A2B10G10R10,
        DrmFormat.Xbgr2101010 => PixmanFormat.X2B10G10R10,
        _ => throw new NotSupportedException($"Format 0x{(uint)format:X8} is not supported by the software renderer."),
    };

    private sealed record TargetEntry(nint Data, PixmanImage Image);
}
