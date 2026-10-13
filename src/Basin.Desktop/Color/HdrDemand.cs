using System.Buffers;
using Basin.Capabilities;

namespace Basin.Desktop;

public sealed class HdrDemand : IToplevelObserver, IDisposable
{
    private readonly IToplevelModel _toplevels;
    private readonly Func<Surface, ImageDescription?> _describe;
    private readonly IEventSource _release;
    private Action? _unhook;
    private bool _disposed;

    public HdrDemand(IToplevelModel toplevels, ICompositorEventLoop loop, Func<Surface, ImageDescription?> describe)
    {
        ArgumentNullException.ThrowIfNull(toplevels);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(describe);
        _toplevels = toplevels;
        _describe = describe;
        _release = loop.AddTimer(OnRelease);
        toplevels.AddObserver(this);
    }

    public static HdrDemand Watch(IToplevelModel toplevels, ColorManager color, ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(color);
        var demand = new HdrDemand(toplevels, loop, color.DescriptionOf);
        void OnDescription(Surface surface, ImageDescription? description) => demand.Reevaluate();
        color.SurfaceDescriptionChanged += OnDescription;
        demand._unhook = () => color.SurfaceDescriptionChanged -= OnDescription;
        demand.Reevaluate();
        return demand;
    }

    public int ReleaseDelayMs { get; set; } = 2000;

    public bool Demand { get; private set; }

    public event Action<bool>? Changed;

    public static bool IsHdr(ImageDescription? description) =>
        description is { TransferNamed: ColorTransferFunction.St2084Pq or ColorTransferFunction.Hlg or ColorTransferFunction.ExtLinear };

    public void Reevaluate()
    {
        if (_disposed)
        {
            return;
        }

        var wanted = Scan();
        if (wanted)
        {
            _release.UpdateTimer(0);
            if (!Demand)
            {
                Demand = true;
                Changed?.Invoke(true);
            }
        }
        else if (Demand)
        {
            _release.UpdateTimer(Math.Max(1, ReleaseDelayMs));
        }
    }

    public void OnToplevelAdded(ulong toplevelId) => Reevaluate();

    public void OnToplevelChanged(ulong toplevelId) => Reevaluate();

    public void OnToplevelRemoved(ulong toplevelId) => Reevaluate();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _unhook?.Invoke();
        _toplevels.RemoveObserver(this);
        _release.Remove();
    }

    private void OnRelease()
    {
        if (_disposed || !Demand || Scan())
        {
            return;
        }

        Demand = false;
        Changed?.Invoke(false);
    }

    private bool Scan()
    {
        var length = 16;
        while (true)
        {
            var buffer = ArrayPool<ToplevelInfo>.Shared.Rent(length);
            try
            {
                var count = _toplevels.Enumerate(buffer);
                if (count < 0)
                {
                    length = buffer.Length * 2;
                    continue;
                }

                for (var i = 0; i < count; i++)
                {
                    if (buffer[i].Surface is { } surface &&
                        (buffer[i].State & ToplevelState.Minimized) == 0 &&
                        IsHdr(_describe(surface)))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                ArrayPool<ToplevelInfo>.Shared.Return(buffer, clearArray: true);
            }
        }
    }
}
