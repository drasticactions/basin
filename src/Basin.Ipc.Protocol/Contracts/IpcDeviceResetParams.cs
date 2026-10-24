using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>devices/reset</c>.
/// </summary>
public sealed class IpcDeviceResetParams : IIpcParams, IIpcReusable
{
    private ulong _device;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcDeviceResetParams"/> class. Every key starts absent, so the deserializer can fill it.
    /// </summary>
    public IpcDeviceResetParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcDeviceResetParams"/> class.
    /// </summary>
    /// <param name="device">The device id.</param>
    /// <param name="keys">The keys to reset, or null for every runtime key.</param>
    public IpcDeviceResetParams(ulong device, IReadOnlyList<string>? keys = null)
    {
        Device = device;
        Keys = keys;
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

    /// <summary>
    /// Gets or sets the setting keys whose runtime values go. <see langword="null"/> resets every runtime key of the device.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Keys { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'device' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _device = default;
        _present = 0;
        Keys = null;
    }
}
