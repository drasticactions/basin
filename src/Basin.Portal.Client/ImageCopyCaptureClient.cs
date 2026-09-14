using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Protocol;
using Basin.Portal.Client.Protocol;
using Pixman;
using Wayland;

namespace Basin.Portal.Client;

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

    public ExtImageCopyCaptureManagerV1 Manager { get; }

    public ZwpLinuxDmabufV1? Dmabuf { get; }

    public string RenderNode { get; set; } = Environment.GetEnvironmentVariable("BASIN_RENDER_NODE") is { Length: > 0 } node ? node : "/dev/dri/renderD128";

    public IAllocator? Allocator => _dmabuf?.Allocator;

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

    internal void OnConstraints(DrmFormatSet constraints)
    {
        if (_dmabufTried || Dmabuf is null || constraints.Count == 0)
        {
            return;
        }

        _dmabufTried = true;
        _dmabuf = DmabufSupport.TryOpen(RenderNode, constraints, Log);
    }

    public bool IsAvailable => _outputSources is not null;

    public bool Supports(in CaptureSource source) => source.Kind switch
    {
        CaptureSourceKind.Output => _outputSources is not null,
        CaptureSourceKind.Toplevel => _toplevelSources is not null,
        CaptureSourceKind.Region => _outputSources is not null,
        CaptureSourceKind.Cursor => false,
        _ => false,
    };

    public bool TryDescribe(in CaptureSource source, out CaptureFormat format)
    {
        format = default;
        if (source.Kind == CaptureSourceKind.Region)
        {
            return RegionStream(source.LayoutBox) is { } region && region.TryDescribe(source.OverlayCursor, out format);
        }

        return Stream(source) is { } stream && stream.TryDescribe(source.OverlayCursor, out format);
    }

    public bool Capture(in CaptureSource source, in Box region, IBuffer target)
    {
        if (source.Kind == CaptureSourceKind.Region)
        {
            return CaptureRegion(source.LayoutBox, target);
        }

        return Stream(source) is { } stream && stream.CaptureInto(target);
    }

    public bool TryCursorState(IOutput output, out CaptureCursorState cursor)
    {
        cursor = default;
        return false;
    }

    public void SetCursor(IBuffer? image, in CaptureCursorState state)
    {
    }

    public void AddDamageObserver(ICaptureDamageObserver observer)
    {
        if (!_observers.Contains(observer))
        {
            _observers.Add(observer);
        }
    }

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
