using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;
using Wayland.Server.Shm;

namespace Basin;

public sealed class ShmGlobal : IDisposable
{
    private static int _advertisedVersion;

    private readonly ManagedShmGlobal? _managed;

    public ShmGlobal(
        WlServerDisplay display,
        ReadOnlySpan<DrmFormat> extraFormats = default,
        ClientBufferRegistry? buffers = null,
        ShmLimits? limits = null,
        ShmAccessPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (!SelectManaged(display))
        {
            LibWaylandShm.Init(display);
            foreach (var format in extraFormats)
            {
                LibWaylandShm.AddFormat(display, format.ToWlShm());
            }

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
        BasinLog.Debug($"wl_shm: managed implementation selected ({_managed.Policy})");
    }

    public ManagedShmGlobal? Managed => _managed;

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
        || !OperatingSystem.IsLinux()
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
