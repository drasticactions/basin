using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>devices/set</c>. The values go to the runtime layer only, and die with their device.
/// </summary>
public sealed class IpcDeviceSetParams : IIpcParams, IIpcReusable
{
    private Dictionary<string, JsonElement>? _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcDeviceSetParams"/> class. Every key starts absent, so the deserializer can fill it.
    /// </summary>
    public IpcDeviceSetParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcDeviceSetParams"/> class.
    /// </summary>
    /// <param name="device">The device id, or null.</param>
    /// <param name="type">The device type word, or null.</param>
    /// <param name="settings">The settings to write.</param>
    public IpcDeviceSetParams(ulong? device, string? type, Dictionary<string, JsonElement> settings)
    {
        Device = device;
        Type = type;
        Settings = settings;
    }

    /// <summary>
    /// Gets or sets the id of the one device to change. A string of decimal digits is read too. <see langword="null"/> uses <see cref="Type"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong? Device { get; set; }

    /// <summary>
    /// Gets or sets the device type whose devices all change. <c>keyboard</c> means every device with keys. <see langword="null"/> uses <see cref="Device"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the setting key to value map. A value is a word, a number, a boolean, or six numbers for <c>calibration_matrix</c>. The key is required.
    /// </summary>
    public Dictionary<string, JsonElement>? Settings
    {
        get => _settings;
        set => _settings = value;
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        _settings is null ? "'settings' is required."
        : Device is null && Type is null ? "'device' or 'type' is required."
        : null;

    void IIpcReusable.Reset()
    {
        Device = null;
        Type = null;
        _settings = null;
    }
}
