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
    /// Gets the buffer size for a source from a capture session. The format is always XRGB8888.
    /// A region source describes the whole output that contains the center of the region.
    /// </summary>
    /// <param name="source">The source to describe.</param>
    /// <param name="format">The buffer size and format, or the default value on failure.</param>
    /// <returns><see langword="false"/> when no session can open or the size does not arrive within one second.</returns>
    public bool TryDescribe(in CaptureSource source, out CaptureFormat format)
    {
        format = default;
        if (source.Kind == CaptureSourceKind.Region)
        {
            return RegionStream(source.LayoutBox) is { } region && region.TryDescribe(source.OverlayCursor, out format);
        }

        return Stream(source) is { } stream && stream.TryDescribe(source.OverlayCursor, out format);
    }

    /// <summary>
    /// Captures one frame of a source into a buffer. A dmabuf target receives the frame directly, and other targets receive a copy from shared memory.
    /// The call ignores <paramref name="region"/>. A region source captures the whole output that contains the center of the region.
    /// </summary>
    /// <param name="source">The source to capture.</param>
    /// <param name="region">The area to capture. It is ignored.</param>
    /// <param name="target">The buffer that receives the frame. It must be at least the size that <see cref="TryDescribe"/> gave.</param>
    /// <returns><see langword="false"/> when the frame fails, times out after one second or does not fit the target.</returns>
    public bool Capture(in CaptureSource source, in Box region, IBuffer target)
    {
        if (source.Kind == CaptureSourceKind.Region)
        {
            return CaptureRegion(source.LayoutBox, target);
        }

        return Stream(source) is { } stream && stream.CaptureInto(target);
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

    private bool CaptureRegion(Box box, IBuffer target) =>
        RegionStream(box) is { } stream && stream.CaptureInto(target);

    private ImageCopyCaptureStream? RegionStream(Box box)
    {
        if (box.Width <= 0 || box.Height <= 0)
        {
            return null;
        }

        var output = _outputs.At(box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0)) ?? First();
        return output is null ? null : Stream(CaptureSource.Output(output));
    }

    private IOutput? First()
    {
        foreach (var (output, _) in _outputs.Layout.Outputs)
        {
            return output;
        }

        return null;
    }
}
