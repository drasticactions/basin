using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>devices/get</c>.
/// </summary>
public sealed class IpcDeviceParams : IIpcParams, IIpcReusable
{
    private ulong _device;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcDeviceParams"/> class. Every key starts absent, so the deserializer can fill it.
    /// </summary>
    public IpcDeviceParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcDeviceParams"/> class.
    /// </summary>
    /// <param name="device">The device id.</param>
    public IpcDeviceParams(ulong device)
    {
        Device = device;
    }

    /// <summary>
    /// Gets or sets the device id from <c>devices/list</c>. A string of decimal digits is read too. The key is required.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Device
    {
        get => _device;
        set
        {
            _device = value;
            _present |= 1;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'device' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _device = default;
        _present = 0;
    }
}
