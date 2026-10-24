using Pixman;

namespace Basin.Render.Pixman;

/// <summary>
/// The software reference renderer. It draws into CPU-accessible shm buffers through pixman.
/// It applies color transforms through a 3D LUT and never signals a render fence.
/// </summary>
public sealed class PixmanRenderer : IRenderer
{
    private readonly Dictionary<IBuffer, TargetEntry> _targets = [];
    private readonly PixmanRenderPass _pass = new();

    /// <inheritdoc/>
    public ITexture? ImportTexture(IBuffer buffer) => Supports(buffer.Format) ? new PixmanTexture(buffer) : null;

    /// <inheritdoc/>
    public IReadOnlyList<DrmFormat> ShmTextureFormats => ShmFormatLists.Integer;

    /// <summary>
    /// Creates a render stack with a new <see cref="PixmanRenderer"/> and no device allocator.
    /// </summary>
    /// <returns>A stack that renders into shm buffers only.</returns>
    public static RenderStack CreateStack() => new(new PixmanRenderer(), null);

    /// <inheritdoc/>
    public ColorTransformCapability ColorTransform => ColorTransformCapability.Lut3D;

    /// <inheritdoc/>
    public RenderFencePrecision FencePrecision => RenderFencePrecision.None;

    /// <inheritdoc/>
    public IColorLut? ImportLut(ColorLut3D lut) => new PixmanColorLut(lut);

    /// <inheritdoc/>
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

        _pass.Begin(target, entry.Image, view.Data, view.Stride, (options.SolidColorLut as PixmanColorLut)?.Lut);
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

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var entry in _targets.Values)
        {
            entry.Image.Dispose();
        }

        _targets.Clear();
        _pass.DisposeScratch();
    }

    internal static bool Supports(DrmFormat format) => ShmFormatLists.Integer.Contains(format);

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
        DrmFormat.Rgb565 => PixmanFormat.R5G6B5,
        _ => throw new NotSupportedException($"Format 0x{(uint)format:X8} is not supported by the software renderer."),
    };

    private sealed record TargetEntry(nint Data, PixmanImage Image);
}
