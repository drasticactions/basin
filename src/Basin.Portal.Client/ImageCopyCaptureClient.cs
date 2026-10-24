using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Protocol;
using Basin.Portal.Client.Protocol;
using Pixman;
using Wayland;

namespace Basin.Portal.Client;

/// <summary>
/// Implements <see cref="IScreenCapture"/> over <c>ext_image_copy_capture_v1</c> for a portal client.
/// Each capture blocks on the display connection for up to one second.
/// </summary>
public sealed class ImageCopyCaptureClient : IScreenCapture, IDisposable
{
    private static readonly BasinLogger Log = BasinLog.For("portal-client");

    private readonly ExtOutputImageCaptureSourceManagerV1? _outputSources;
    private readonly ExtForeignToplevelImageCaptureSourceManagerV1? _toplevelSources;
    private readonly WlShm _shm;
    private readonly ClientOutputs _outputs;
    private readonly ForeignToplevelList? _toplevels;
    private readonly WlDisplay _display;
    private readonly List<ICaptureDamageObserver> _observers = [];
    private readonly Dictionary<string, ImageCopyCaptureStream> _streams = [];
    private DmabufSupport? _dmabuf;
    private bool _dmabufTried;
    private ClientOutput[] _regionOutputs = [];
    private Box[] _regionBoxes = [];
    private double[] _regionScales = [];
    private RegionPart[] _regionParts = [];
    private PixmanImage? _targetImage;
    private nint _targetData;
    private int _targetWidth;
    private int _targetHeight;
    private int _targetStride;
    private DrmFormat _targetFormat;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCopyCaptureClient"/> class.
    /// </summary>
    /// <param name="manager">The bound image copy capture manager.</param>
    /// <param name="outputSources">The output source manager, or <see langword="null"/> to turn off output and region capture.</param>
    /// <param name="toplevelSources">The toplevel source manager, or <see langword="null"/> to turn off window capture.</param>
    /// <param name="shm">The shared-memory global for capture buffers.</param>
    /// <param name="dmabuf">The dmabuf global, or <see langword="null"/> to capture only through shared memory.</param>
    /// <param name="outputs">The outputs of the compositor.</param>
    /// <param name="toplevels">The toplevel list that maps toplevel ids to handles, or <see langword="null"/>.</param>
    /// <param name="display">The display connection that captures dispatch while they wait.</param>
    public ImageCopyCaptureClient(
        ExtImageCopyCaptureManagerV1 manager,
        ExtOutputImageCaptureSourceManagerV1? outputSources,
        ExtForeignToplevelImageCaptureSourceManagerV1? toplevelSources,
        WlShm shm,
        ZwpLinuxDmabufV1? dmabuf,
        ClientOutputs outputs,
        ForeignToplevelList? toplevels,
        WlDisplay display)
    {
        Manager = manager;
        _outputSources = outputSources;
        _toplevelSources = toplevelSources;
        _shm = shm;
        Dmabuf = dmabuf;
        _outputs = outputs;
        _toplevels = toplevels;
        _display = display;
    }

    /// <summary>
    /// Gets the bound image copy capture manager.
    /// </summary>
    public ExtImageCopyCaptureManagerV1 Manager { get; }

    /// <summary>
    /// Gets the dmabuf global, or <see langword="null"/> when the compositor has none.
    /// </summary>
    public ZwpLinuxDmabufV1? Dmabuf { get; }

    /// <summary>
    /// Gets or sets the DRM render node that the dmabuf allocator opens. The default comes from <c>BASIN_RENDER_NODE</c>, or else it is <c>/dev/dri/renderD128</c>.
    /// A change after the first dmabuf constraints arrive has no effect.
    /// </summary>
    public string RenderNode { get; set; } = Environment.GetEnvironmentVariable("BASIN_RENDER_NODE") is { Length: > 0 } node ? node : "/dev/dri/renderD128";

    /// <summary>
    /// Gets an allocator for buffers that the compositor can capture into. It is <see langword="null"/> until dmabuf constraints arrive and a GBM device opens.
    /// </summary>
    public IAllocator? Allocator => _dmabuf?.Allocator;

    /// <summary>
    /// Starts a capture session on the first output so that the compositor sends its dmabuf constraints. This can set <see cref="Allocator"/> and <see cref="CanCaptureYuv"/>.
    /// It does nothing without a dmabuf global.
    /// </summary>
    public void ProbeDmabuf()
    {
        if (Dmabuf is null)
        {
            return;
        }

        foreach (var (output, _) in _outputs.Layout.Outputs)
        {
            if (Stream(CaptureSource.Output(output)) is { } stream)
            {
                stream.TryDescribe(false, out _);
            }

            return;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a capture session offered NV12 with the linear modifier.
    /// </summary>
    public bool CanCaptureYuv { get; private set; }

    internal void OnConstraints(DrmFormatSet constraints)
    {
        CanCaptureYuv |= constraints.Contains(DrmFormat.Nv12, DrmFormatSet.ModifierLinear);
        if (_dmabufTried || Dmabuf is null || constraints.Count == 0)
        {
            return;
        }

        _dmabufTried = true;
        _dmabuf = DmabufSupport.TryOpen(RenderNode, constraints, Log);
    }

    /// <summary>
    /// Gets a value indicating whether output capture is possible. It is <see langword="false"/> when the compositor has no output source manager.
    /// </summary>
    public bool IsAvailable => _outputSources is not null;

    /// <summary>
    /// Tells whether the compositor has the source manager that a source kind needs. Cursor capture is never supported.
    /// </summary>
    /// <param name="source">The source to check.</param>
    /// <returns><see langword="true"/> when the matching source manager is bound.</returns>
    public bool Supports(in CaptureSource source) => source.Kind switch
    {
        CaptureSourceKind.Output => _outputSources is not null,
        CaptureSourceKind.Toplevel => _toplevelSources is not null,
        CaptureSourceKind.Region => _outputSources is not null,
        CaptureSourceKind.Cursor => false,
        _ => false,
    };

    /// <summary>
    /// Gets the buffer size for a source. The format is always XRGB8888.
    /// A region source is the box in buffer pixels at its scale, or at the largest scale of the outputs that the box touches when its scale is 0.
    /// </summary>
    /// <param name="source">The source to describe.</param>
    /// <param name="format">The buffer size and format, or the default value on failure.</param>
    /// <returns><see langword="false"/> when no session can open, the size does not arrive within one second, or a region touches no output.</returns>
    public bool TryDescribe(in CaptureSource source, out CaptureFormat format)
    {
        format = default;
        if (source.Kind == CaptureSourceKind.Region)
        {
            var count = CollectOutputs();
            var box = source.LayoutBox;
            if (box.IsEmpty || !Touches(box, count))
            {
                return false;
            }

            var size = RegionCapturePlan.Size(box, RegionCapturePlan.TargetScale(box, _regionBoxes.AsSpan(0, count), _regionScales.AsSpan(0, count), source.Scale));
            format = new CaptureFormat(size.Width, size.Height, DrmFormat.Xrgb8888);
            return size.Width > 0 && size.Height > 0;
        }

        return Stream(source) is { } stream && stream.TryDescribe(source.OverlayCursor, out format);
    }

    /// <summary>
    /// Captures one frame of a source into a buffer. A dmabuf target receives a whole output or toplevel directly, and other targets receive a copy from shared memory.
    /// A region source captures each output that the box touches and composites the parts at the scale that <see cref="TryDescribe"/> gave. Parts outside every output are transparent black.
    /// </summary>
    /// <param name="source">The source to capture.</param>
    /// <param name="region">The area to copy in source buffer pixels, or an empty box for the whole source.</param>
    /// <param name="target">The buffer that receives the frame. It must be at least the size of the region, or of the source when the region is empty.</param>
    /// <returns><see langword="false"/> when a frame fails, times out after one second or does not fit the target.</returns>
    public bool Capture(in CaptureSource source, in Box region, IBuffer target)
    {
        if (source.Kind == CaptureSourceKind.Region)
        {
            return CaptureRegion(source, region, target);
        }

        if (Stream(source) is not { } stream)
        {
            return false;
        }

        if (region.IsEmpty)
        {
            return stream.CaptureInto(target);
        }

        if (!stream.CaptureToScratch(source.OverlayCursor) || stream.ScratchImage() is not { } scratch)
        {
            return false;
        }

        var crop = region.Intersect(new Box(0, 0, stream.Width, stream.Height));
        Span<RegionPart> part = [new RegionPart(0, new Box(0, 0, crop.Width, crop.Height), crop.X, crop.Y, 1)];
        return Composite(target, region.Width, region.Height, part, scratch);
    }

    /// <summary>
    /// Reports no cursor state, because this client cannot capture the cursor separately.
    /// </summary>
    /// <param name="output">The output. It is ignored.</param>
    /// <param name="cursor">Always the default value.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool TryCursorState(IOutput output, out CaptureCursorState cursor)
    {
        cursor = default;
        return false;
    }

    /// <summary>
    /// Does nothing. The compositor draws its own cursor into the capture when the source asks for it.
    /// </summary>
    /// <param name="image">The cursor image. It is ignored.</param>
    /// <param name="state">The cursor state. It is ignored.</param>
    public void SetCursor(IBuffer? image, in CaptureCursorState state)
    {
    }

    /// <summary>
    /// Adds an observer that is told after each successful frame. An observer that is present already is not added again.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    public void AddDamageObserver(ICaptureDamageObserver observer)
    {
        if (!_observers.Contains(observer))
        {
            _observers.Add(observer);
        }
    }

    /// <inheritdoc/>
    public void RemoveDamageObserver(ICaptureDamageObserver observer) => _observers.Remove(observer);

    internal void NotifyDamaged(IOutput? output, Box box)
    {
        foreach (var observer in _observers.ToArray())
        {
            if (output is not null)
            {
                observer.OnSourceDamaged(output, box);
            }
            else
            {
                foreach (var (candidate, _) in _outputs.Layout.Outputs)
                {
                    observer.OnSourceDamaged(candidate, box);
                }
            }
        }
    }

    /// <summary>
    /// Disposes every capture stream and the dmabuf allocator.
    /// </summary>
    public void Dispose()
    {
        foreach (var stream in _streams.Values)
        {
            stream.Dispose();
        }

        _streams.Clear();
        _targetImage?.Dispose();
        _targetImage = null;
        _dmabuf?.Dispose();
    }

    private ImageCopyCaptureStream? Stream(in CaptureSource source, bool create = true)
    {
        var key = source.Kind switch
        {
            CaptureSourceKind.Output when source.OutputTarget is { } output => $"output:{output.Name}",
            CaptureSourceKind.Toplevel => $"toplevel:{source.ToplevelId}",
            _ => "",
        };
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (_streams.TryGetValue(key, out var existing))
        {
            return existing;
        }

        if (!create)
        {
            return null;
        }

        var wireSource = MakeSource(source);
        if (wireSource is null)
        {
            return null;
        }

        var stream = new ImageCopyCaptureStream(this, _shm, _display, wireSource, source.OutputTarget);
        _streams[key] = stream;
        return stream;
    }

    private ExtImageCaptureSourceV1? MakeSource(in CaptureSource source)
    {
        switch (source.Kind)
        {
            case CaptureSourceKind.Output when source.OutputTarget is ClientOutput output && _outputSources is { } manager:
                return manager.CreateSource(output.Proxy);
            case CaptureSourceKind.Toplevel when _toplevelSources is { } manager && _toplevels?.HandleOf(source.ToplevelId) is { } handle:
                return manager.CreateSource(handle);
            default:
                return null;
        }
    }

    private bool CaptureRegion(in CaptureSource source, in Box crop, IBuffer target)
    {
        var count = CollectOutputs();
        var box = source.LayoutBox;
        if (box.IsEmpty || !Touches(box, count))
        {
            return false;
        }

        var boxes = _regionBoxes.AsSpan(0, count);
        var scales = _regionScales.AsSpan(0, count);
        var scale = RegionCapturePlan.TargetScale(box, boxes, scales, source.Scale);
        var size = RegionCapturePlan.Size(box, scale);
        var width = crop.IsEmpty ? size.Width : crop.Width;
        var height = crop.IsEmpty ? size.Height : crop.Height;
        if (_regionParts.Length < count)
        {
            _regionParts = new RegionPart[count];
        }

        var parts = _regionParts.AsSpan(0, RegionCapturePlan.Plan(box, boxes, scales, scale, crop, _regionParts));
        if (!BeginTarget(target, width, height))
        {
            return false;
        }

        var ok = true;
        foreach (var part in parts)
        {
            if (Stream(CaptureSource.Output(_regionOutputs[part.Output])) is not { } stream ||
                !stream.CaptureToScratch(source.OverlayCursor) ||
                stream.ScratchImage() is not { } scratch)
            {
                ok = false;
                break;
            }

            CompositePart(scratch, part);
        }

        target.EndDataAccess();
        return ok;
    }

    private bool Composite(IBuffer target, int width, int height, ReadOnlySpan<RegionPart> parts, PixmanImage source)
    {
        if (!BeginTarget(target, width, height))
        {
            return false;
        }

        foreach (var part in parts)
        {
            CompositePart(source, part);
        }

        target.EndDataAccess();
        return true;
    }

    private unsafe bool BeginTarget(IBuffer target, int width, int height)
    {
        if (target.Width < width || target.Height < height || !target.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            return false;
        }

        for (var y = 0; y < target.Height; y++)
        {
            new Span<byte>((byte*)view.Data + (y * view.Stride), target.Width * 4).Clear();
        }

        if (_targetImage is null || _targetData != view.Data || _targetWidth != target.Width || _targetHeight != target.Height ||
            _targetStride != view.Stride || _targetFormat != view.Format)
        {
            _targetImage?.Dispose();
            _targetImage = PixmanImage.CreateBits(
                view.Format == DrmFormat.Argb8888 ? PixmanFormat.A8R8G8B8 : PixmanFormat.X8R8G8B8, target.Width, target.Height, view.Data, view.Stride);
            _targetData = view.Data;
            _targetWidth = target.Width;
            _targetHeight = target.Height;
            _targetStride = view.Stride;
            _targetFormat = view.Format;
        }

        return true;
    }

    private void CompositePart(PixmanImage source, in RegionPart part)
    {
        var destination = part.Destination;
        if (part.Ratio == 1 && part.SourceX == Math.Floor(part.SourceX) && part.SourceY == Math.Floor(part.SourceY))
        {
            _targetImage!.Composite(PixmanOp.Src, source, null, (int)part.SourceX, (int)part.SourceY, 0, 0, destination.X, destination.Y, destination.Width, destination.Height);
            return;
        }

        var translate = PixmanTransform.CreateTranslate(part.SourceX, part.SourceY);
        var scale = PixmanTransform.CreateScale(part.Ratio, part.Ratio);
        var transform = PixmanTransform.Multiply(in translate, in scale);
        source.SetTransform(in transform);
        source.SetFilter(part.Ratio < 1 ? PixmanFilter.Bilinear : PixmanFilter.Nearest);
        source.SetRepeat(PixmanRepeat.Pad);
        _targetImage!.Composite(PixmanOp.Src, source, null, 0, 0, 0, 0, destination.X, destination.Y, destination.Width, destination.Height);
        source.ClearTransform();
        source.SetFilter(PixmanFilter.Nearest);
        source.SetRepeat(PixmanRepeat.None);
    }

    private int CollectOutputs()
    {
        var count = 0;
        foreach (var (output, _) in _outputs.Layout.Outputs)
        {
            if (output is ClientOutput)
            {
                count++;
            }
        }

        if (_regionOutputs.Length < count)
        {
            _regionOutputs = new ClientOutput[count];
            _regionBoxes = new Box[count];
            _regionScales = new double[count];
        }

        var i = 0;
        foreach (var (output, _) in _outputs.Layout.Outputs)
        {
            if (output is ClientOutput client && i < count)
            {
                _regionOutputs[i] = client;
                _regionBoxes[i] = _outputs.Layout.BoxOf(output);
                _regionScales[i] = output.Scale;
                i++;
            }
        }

        return count;
    }

    private bool Touches(Box box, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (!_regionBoxes[i].Intersect(box).IsEmpty)
            {
                return true;
            }
        }

        return false;
    }
}
