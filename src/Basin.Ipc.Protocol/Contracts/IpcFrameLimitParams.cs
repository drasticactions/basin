using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>windows/frame-limit</c>. Both <c>id</c> and <c>fps</c> are required.
/// </summary>
public sealed class IpcFrameLimitParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private int _fps;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcFrameLimitParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcFrameLimitParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcFrameLimitParams"/> class.
    /// Both keys are present.
    /// </summary>
    /// <param name="id">The window to limit.</param>
    /// <param name="fps">The limit in frames per second, from 0 to 1000.</param>
    public IpcFrameLimitParams(ulong id, int fps)
    {
        Id = id;
        Fps = fps;
    }

    /// <summary>
    /// Gets or sets the id of the window to limit.
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

    /// <summary>
    /// Gets or sets the frame limit in frames per second.
    /// The server accepts 0 to 1000, and 0 removes the limit.
    /// </summary>
    public int Fps
    {
        get => _fps;
        set
        {
            _fps = value;
            _present |= 2;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'fps' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _fps = default;
        _present = 0;
    }
}
