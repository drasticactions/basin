using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

/// <summary>
/// The river-input-management-v1 global. It holds the map from input devices to seat names and reports the changes that the window manager asks for.
/// It does not open devices or move them itself. The consumer registers its devices and applies the reported settings.
/// </summary>
public sealed class RiverInputManager : IDisposable, IRiverInputManagerV1Handler
{
    /// <summary>
    /// The name of the seat that always exists. It takes every device whose seat is destroyed or unknown.
    /// </summary>
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

    /// <summary>
    /// Gets a value indicating whether a window manager holds a live binding to this global.
    /// </summary>
    public bool IsBound => _resource is { IsDestroyed: false };

    /// <summary>
    /// Gets the names of the seats that exist. The list always contains <see cref="DefaultSeatName"/>.
    /// </summary>
    public IReadOnlyList<string> SeatNames => _seatNames;

    /// <summary>
    /// Occurs when the window manager creates a seat with a new, non-empty name. The argument is the seat name.
    /// </summary>
    public event Action<string>? SeatCreated;

    /// <summary>
    /// Occurs when a seat other than <see cref="DefaultSeatName"/> goes away. The argument is the seat name.
    /// It fires after the seat's devices move to the default seat, and for each extra seat when a new window manager replaces the old one.
    /// </summary>
    public event Action<string>? SeatDestroyed;

    /// <summary>
    /// Occurs when a device moves to a different seat. The arguments are the device handle and the new seat name.
    /// A request for a seat that does not exist moves the device to <see cref="DefaultSeatName"/>.
    /// </summary>
    public event Action<object, string>? DeviceAssigned;

    /// <summary>
    /// Occurs when the window manager sets the key repeat of a device. The arguments are the device handle, the rate in keys per second and the delay in milliseconds.
    /// Both values are 0 or more.
    /// </summary>
    public event Action<object, int, int>? RepeatInfoChanged;

    /// <summary>
    /// Occurs when the window manager sets the scroll factor of a device. The arguments are the device handle and the factor, which is 0 or more.
    /// </summary>
    public event Action<object, double>? ScrollFactorChanged;

    /// <summary>
    /// Occurs when the window manager maps a device to an output. The arguments are the device handle and the output.
    /// The output is <see langword="null"/> when the request clears the map or names an output that the compositor does not know.
    /// </summary>
    public event Action<object, IOutput?>? MappedToOutput;

    /// <summary>
    /// Occurs when the window manager maps a device to a rectangle in the logical layout. The arguments are the device handle and the rectangle.
    /// The width and height of the rectangle are more than 0.
    /// </summary>
    public event Action<object, Box>? MappedToRectangle;

    /// <summary>
    /// Registers a device and announces it to the bound window manager. A handle that is already registered does nothing.
    /// The device starts on <see cref="DefaultSeatName"/>.
    /// </summary>
    /// <param name="handle">The consumer's object for the device. The events give it back by reference.</param>
    /// <param name="name">The device name that the window manager matches its rules on.</param>
    /// <param name="type">The kind of device.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handle"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Unregisters a device and tells the window manager that it is gone. An unknown handle does nothing.
    /// </summary>
    /// <param name="handle">The handle given to <see cref="AddDevice(object, string, InputDeviceType)"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handle"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Finds the seat that a device is assigned to.
    /// </summary>
    /// <param name="handle">The handle given to <see cref="AddDevice(object, string, InputDeviceType)"/>.</param>
    /// <returns>The seat name, or <see cref="DefaultSeatName"/> for a handle that is not registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handle"/> is <see langword="null"/>.</exception>
    public string SeatOf(object handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return _devices.Find(d => ReferenceEquals(d.Handle, handle))?.SeatName ?? DefaultSeatName;
    }

    /// <summary>
    /// Forgets every device and every extra seat, and removes the global. It raises no events. A second call does nothing.
    /// </summary>
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

    internal RiverInputDeviceV1Resource? ResourceFor(object handle, WlClient client) =>
        _devices.Find(d => ReferenceEquals(d.Handle, handle))?.Resource is { IsDestroyed: false } resource &&
        ReferenceEquals(resource.Client, client)
            ? resource
            : null;

    private void Announce(RiverInputDevice device)
    {
        if (_resource is not { IsDestroyed: false } manager)
        {
            return;
        }

        var resource = device.Bind(manager.Client, manager.Version, 0);
        manager.SendInputDevice(resource);
        device.SendProperties(manager.Version);
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new RiverInputManagerV1Resource(client, version, id, this);
        _resource = resource;

        resource.Destroyed += (_, _) => _resource = null;

        foreach (var device in _devices)
        {
            Announce(device);
        }
    }

    void IRiverInputManagerV1Handler.CreateSeat(RiverInputManagerV1Resource resource, in RiverInputManagerV1Resource.CreateSeatEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Name) || _seatNames.Contains(args.Name))
        {
            return;
        }

        _seatNames.Add(args.Name);
        SeatCreated?.Invoke(args.Name);
    }

    void IRiverInputManagerV1Handler.DestroySeat(RiverInputManagerV1Resource resource, in RiverInputManagerV1Resource.DestroySeatEventArgs args)
    {
        if (args.Name == DefaultSeatName || !_seatNames.Remove(args.Name))
        {
            return;
        }

        foreach (var device in _devices)
        {
            if (device.SeatName == args.Name)
            {
                device.AssignTo(DefaultSeatName);
            }
        }

        SeatDestroyed?.Invoke(args.Name);
    }

    void IRiverInputManagerV1Handler.Stop(RiverInputManagerV1Resource resource)
    {
        resource.SendFinished();
        _resource = null;
    }

    void IRiverInputManagerV1Handler.Destroy(RiverInputManagerV1Resource resource) => _resource = null;
}
