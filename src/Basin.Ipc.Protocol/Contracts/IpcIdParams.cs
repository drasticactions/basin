using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of a method that names one window or one workspace by its id, for example <c>windows/activate</c>.
/// </summary>
public sealed class IpcIdParams : IIpcParams, IIpcReusable
{
    private ulong _id;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcIdParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcIdParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcIdParams"/> class.
    /// The <c>id</c> key is present.
    /// </summary>
    /// <param name="id">The window or workspace id.</param>
    public IpcIdParams(ulong id)
    {
        Id = id;
    }

    /// <summary>
    /// Gets or sets the window or workspace id that the method acts on.
    /// JSON can carry it as a number or as a string of decimal digits.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Id
    {
        get => _id;
        set
        {
            _id = value;
            _present |= 1;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required."
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _present = 0;
    }
}
