using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;
using Wayland.Server.Shm;
using static Basin.Diagnostics.CoreLog;

namespace Basin;

/// <summary>
/// The <c>wl_shm</c> global. It uses libwayland's implementation on a libwayland transport with local clients, and <see cref="ManagedShmGlobal"/> otherwise or when <c>BASIN_MANAGED_SHM</c> asks for it.
/// </summary>
public sealed class ShmGlobal : IDisposable
{
    private static int _advertisedVersion;

    private readonly ManagedShmGlobal? _managed;
    private readonly WlServerDisplay _display;
    private readonly HashSet<DrmFormat> _libwaylandFormats = [DrmFormat.Argb8888, DrmFormat.Xrgb8888];

    /// <summary>
    /// Initializes a new instance of the <see cref="ShmGlobal"/> class and selects the implementation.
    /// </summary>
    /// <param name="display">The display that gets the global.</param>
    /// <param name="extraFormats">The formats to advertise beyond ARGB8888 and XRGB8888.</param>
    /// <param name="buffers">The registry for managed buffers. The managed implementation requires it.</param>
    /// <param name="limits">The per-client caps for the managed implementation, or <see langword="null"/> for the defaults.</param>
    /// <param name="policy">The managed access policy, or <see langword="null"/> to read it from <c>BASIN_MANAGED_SHM</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The managed implementation is selected and <paramref name="buffers"/> is <see langword="null"/>.</exception>
    public ShmGlobal(
        WlServerDisplay display,
        ReadOnlySpan<DrmFormat> extraFormats = default,
        ClientBufferRegistry? buffers = null,
        ShmLimits? limits = null,
        ShmAccessPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        _display = display;
        if (!SelectManaged(display))
        {
            LibWaylandShm.Init(display);
            Advertise(extraFormats);
            return;
        }

        if (buffers is null)
        {
            throw new InvalidOperationException(
                "managed wl_shm needs the ClientBufferRegistry the compositor resolves attaches through.");
        }

        var effectivePolicy = policy
            ?? (EnvironmentSelection() == "guarded" ? ShmAccessPolicy.Guarded : ShmAccessPolicy.Direct);
        _managed = new ManagedShmGlobal(
            display,
            SharedMemory.SupportsPlatformMemory ? SharedMemory.CreateForPlatform() : null,
            buffers,
            effectivePolicy,
            limits,
            extraFormats);
        Log.Debug($"wl_shm: managed implementation selected ({_managed.Policy})");
    }

    /// <summary>Gets the managed implementation, or <see langword="null"/> when libwayland's implementation is in use.</summary>
    public ManagedShmGlobal? Managed => _managed;

    /// <summary>
    /// Adds formats to the advertised list of the selected implementation.
    /// </summary>
    /// <param name="formats">The formats to add. Duplicates are ignored.</param>
    public void Advertise(ReadOnlySpan<DrmFormat> formats)
    {
        if (_managed is not null)
        {
            _managed.Advertise(formats);
            return;
        }

        foreach (var format in formats)
        {
            if (_libwaylandFormats.Add(format))
            {
                LibWaylandShm.AddFormat(_display, format.ToWlShm());
            }
        }
    }

    /// <summary>
    /// Adds formats to the advertised list of the selected implementation.
    /// </summary>
    /// <param name="formats">The list of formats to add. Duplicates are ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="formats"/> is <see langword="null"/>.</exception>
    public void Advertise(IReadOnlyList<DrmFormat> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        foreach (var format in formats)
        {
            Advertise([format]);
        }
    }

    /// <summary>
    /// Disposes the managed implementation. The libwayland global lives as long as the display.
    /// </summary>
    public void Dispose() => _managed?.Dispose();

    /// <summary>
    /// Gets the <c>wl_shm</c> version that a new <see cref="ShmGlobal"/> will advertise. The environment selection decides it, not the transport.
    /// </summary>
    public static int AdvertisedVersion
    {
        get
        {
            if (SelectManagedByEnvironment())
            {
                return ManagedShmGlobal.Version;
            }

            if (_advertisedVersion == 0)
            {
                _advertisedVersion = ReadAdvertisedVersion();
            }

            return _advertisedVersion;
        }
    }

    private static bool SelectManaged(WlServerDisplay display) =>
        display.Transport is not LibWaylandTransport
        || !PlatformFacts.HasLocalClients
        || SelectManagedByEnvironment();

    private static bool SelectManagedByEnvironment() =>
        EnvironmentSelection() is "1" or "true" or "guarded";

    private static string? EnvironmentSelection() =>
        Environment.GetEnvironmentVariable("BASIN_MANAGED_SHM");

    private const int InitShmVersion = 2;

    private static unsafe int ReadAdvertisedVersion()
    {
        nint symbol = 0;
        if (NativeLibrary.TryLoad("libwayland-server.so.0", out var library))
        {
            NativeLibrary.TryGetExport(library, "wl_shm_interface", out symbol);
        }

        var declared = symbol != 0 ? *(int*)(symbol + nint.Size) : 1;
        return Math.Min(declared, InitShmVersion);
    }
}
