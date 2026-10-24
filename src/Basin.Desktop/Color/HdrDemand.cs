using System.Buffers;
using Basin.Capabilities;

namespace Basin.Desktop;

/// <summary>
/// Tracks whether a visible toplevel shows HDR content. The demand rises at once and falls only after a release delay.
/// </summary>
public sealed class HdrDemand : IToplevelObserver, IDisposable
{
    private readonly IToplevelModel _toplevels;
    private readonly Func<Surface, ImageDescription?> _describe;
    private readonly IEventSource _release;
    private Action? _unhook;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HdrDemand"/> class and starts to observe the toplevels. It does not evaluate the demand.
    /// </summary>
    /// <param name="toplevels">The toplevel model to scan.</param>
    /// <param name="loop">The loop that holds the release timer.</param>
    /// <param name="describe">The function that returns a surface's image description.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Creates a demand that also re-evaluates when a surface description changes, and evaluates it once.
    /// </summary>
    /// <param name="toplevels">The toplevel model to scan.</param>
    /// <param name="color">The color manager that supplies the surface descriptions.</param>
    /// <param name="loop">The loop that holds the release timer.</param>
    /// <returns>A new demand. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Gets or sets the time in milliseconds that the demand stays on after the last HDR toplevel goes. The minimum is 1.
    /// </summary>
    public int ReleaseDelayMs { get; set; } = 2000;

    /// <summary>
    /// Gets a value indicating whether a toplevel that is not minimized has an HDR description, or had one within the release delay.
    /// </summary>
    public bool Demand { get; private set; }

    /// <summary>
    /// Occurs when <see cref="Demand"/> changes. The argument is the new value.
    /// </summary>
    public event Action<bool>? Changed;

    /// <summary>
    /// Tests whether a description uses the PQ, HLG or extended linear transfer function.
    /// </summary>
    /// <param name="description">The description to test, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for an HDR transfer function, otherwise <see langword="false"/>.</returns>
    public static bool IsHdr(ImageDescription? description) =>
        description is { TransferNamed: ColorTransferFunction.St2084Pq or ColorTransferFunction.Hlg or ColorTransferFunction.ExtLinear };

    /// <summary>
    /// Scans the toplevels. It raises the demand at once, or starts the release timer when no HDR toplevel is left. It does nothing after disposal.
    /// </summary>
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

    /// <inheritdoc/>
    public void OnToplevelAdded(ulong toplevelId) => Reevaluate();

    /// <inheritdoc/>
    public void OnToplevelChanged(ulong toplevelId) => Reevaluate();

    /// <inheritdoc/>
    public void OnToplevelRemoved(ulong toplevelId) => Reevaluate();

    /// <summary>
    /// Stops the observation and removes the release timer. <see cref="Changed"/> does not occur after this call.
    /// </summary>
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
