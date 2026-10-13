using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;
using Wayland.Server.Shm;
using static Basin.Diagnostics.CoreLog;

namespace Basin;

public sealed class ShmGlobal : IDisposable
{
    private static int _advertisedVersion;

    private readonly ManagedShmGlobal? _managed;
    private readonly WlServerDisplay _display;
    private readonly HashSet<DrmFormat> _libwaylandFormats = [DrmFormat.Argb8888, DrmFormat.Xrgb8888];

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

    public ManagedShmGlobal? Managed => _managed;

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

    public void Advertise(IReadOnlyList<DrmFormat> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        foreach (var format in formats)
        {
            Advertise([format]);
        }
    }

    public void Dispose() => _managed?.Dispose();

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
