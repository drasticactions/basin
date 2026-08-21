using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

public sealed class RiverInputManager : IDisposable
{
    public const string DefaultSeatName = "default";

    private readonly RiverWindowManager _manager;
    private readonly WlGlobal _global;
    private readonly List<string> _seatNames = [DefaultSeatName];
    private readonly List<RiverInputDevice> _devices = [];
    private RiverInputManagerV1Resource? _resource;
    private bool _disposed;

    internal RiverInputManager(RiverWindowManager manager, WlServerDisplay display)
    {
        _manager = manager;
        _global = display.CreateGlobal(
            RiverInputManagerV1.Interface,
            RiverInputManagerV1.Interface.Version,
            OnBind);
    }

    public bool IsBound => _resource is { IsDestroyed: false };

    public IReadOnlyList<string> SeatNames => _seatNames;

    public event Action<string>? SeatCreated;

    public event Action<string>? SeatDestroyed;

    public event Action<object, string>? DeviceAssigned;

    public event Action<object, int, int>? RepeatInfoChanged;

    public event Action<object, double>? ScrollFactorChanged;

    public event Action<object, IOutput?>? MappedToOutput;

    public event Action<object, Box>? MappedToRectangle;

    public void AddDevice(object handle, string name, InputDeviceType type)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(name);
        if (_devices.Exists(d => ReferenceEquals(d.Handle, handle)))
        {
            return;
        }

        var device = new RiverInputDevice(this, handle, name, type);
        _devices.Add(device);
        Announce(device);
    }

    public void RemoveDevice(object handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (_devices.Find(d => ReferenceEquals(d.Handle, handle)) is not { } device)
        {
            return;
        }

        _devices.Remove(device);
        device.SendRemoved();
    }

    public string SeatOf(object handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return _devices.Find(d => ReferenceEquals(d.Handle, handle))?.SeatName ?? DefaultSeatName;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _devices.Clear();
        _seatNames.Clear();
        _seatNames.Add(DefaultSeatName);
        _global.Dispose();
    }

    internal void ResetForNewManager()
    {
        _resource = null;

        for (var i = _seatNames.Count - 1; i >= 0; i--)
        {
            if (_seatNames[i] != DefaultSeatName)
            {
                var name = _seatNames[i];
                _seatNames.RemoveAt(i);
                SeatDestroyed?.Invoke(name);
            }
        }

        foreach (var device in _devices)
        {
            device.ResetForNewManager();
        }
    }

    internal void RaiseAssigned(RiverInputDevice device) =>
        DeviceAssigned?.Invoke(device.Handle, device.SeatName);

    internal void RaiseRepeatInfo(RiverInputDevice device, int rate, int delay) =>
        RepeatInfoChanged?.Invoke(device.Handle, rate, delay);

    internal void RaiseScrollFactor(RiverInputDevice device, double factor) =>
        ScrollFactorChanged?.Invoke(device.Handle, factor);

    internal void RaiseMappedToOutput(RiverInputDevice device, IOutput? output) =>
        MappedToOutput?.Invoke(device.Handle, output);

    internal void RaiseMappedToRectangle(RiverInputDevice device, in Box rectangle) =>
        MappedToRectangle?.Invoke(device.Handle, rectangle);

    internal bool HasSeat(string name) => _seatNames.Contains(name);

    internal void CreateSeatForTest(string name)
    {
        if (!string.IsNullOrEmpty(name) && !_seatNames.Contains(name))
        {
            _seatNames.Add(name);
            SeatCreated?.Invoke(name);
        }
    }

    internal void DestroySeatForTest(string name)
    {
        if (name == DefaultSeatName || !_seatNames.Remove(name))
        {
            return;
        }

        foreach (var device in _devices)
        {
            if (device.SeatName == name)
            {
                device.AssignTo(DefaultSeatName);
            }
        }

        SeatDestroyed?.Invoke(name);
    }

    internal void AssignForTest(object handle, string seatName)
    {
        if (_devices.Find(d => ReferenceEquals(d.Handle, handle)) is { } device)
        {
            device.AssignTo(HasSeat(seatName) ? seatName : DefaultSeatName);
        }
    }

    internal RiverWindowManager Manager => _manager;

    private void Announce(RiverInputDevice device)
    {
        if (_resource is not { IsDestroyed: false } manager)
        {
            return;
        }

        var resource = new RiverInputDeviceV1Resource(manager.Client, manager.Version, 0);
        device.Bind(resource);
        manager.SendInputDevice(resource);
        device.SendProperties(manager.Version);
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new RiverInputManagerV1Resource(client, version, id);
        _resource = resource;

        resource.CreateSeat += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Name) || _seatNames.Contains(e.Name))
            {
                return;
            }

            _seatNames.Add(e.Name);
            SeatCreated?.Invoke(e.Name);
        };

        resource.DestroySeat += (_, e) =>
        {
            if (e.Name == DefaultSeatName || !_seatNames.Remove(e.Name))
            {
                return;
            }

            foreach (var device in _devices)
            {
                if (device.SeatName == e.Name)
                {
                    device.AssignTo(DefaultSeatName);
                }
            }

            SeatDestroyed?.Invoke(e.Name);
        };

        resource.Stop += (_, _) =>
        {
            resource.SendFinished();
            _resource = null;
        };
        resource.DestroyRequest += (_, _) => _resource = null;
        resource.Destroyed += (_, _) => _resource = null;

        foreach (var device in _devices)
        {
            Announce(device);
        }
    }
}
