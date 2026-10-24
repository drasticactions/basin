using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>process/kill</c>. The <c>launch_id</c> key is required.
/// </summary>
public sealed class IpcProcessKillParams : IIpcParams, IIpcReusable
{
    private long _launchId;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcProcessKillParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcProcessKillParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcProcessKillParams"/> class.
    /// The <c>launch_id</c> key is present.
    /// </summary>
    /// <param name="launchId">The launch to end.</param>
    /// <param name="graceMs">The grace time in milliseconds, or <see langword="null"/> for the default.</param>
    public IpcProcessKillParams(long launchId, long? graceMs = null)
    {
        LaunchId = launchId;
        GraceMs = graceMs;
    }

    /// <summary>
    /// Gets or sets the launch id of the process group to end.
    /// </summary>
    public long LaunchId
    {
        get => _launchId;
        set
        {
            _launchId = value;
            _present |= 1;
        }
    }

    /// <summary>
    /// Gets or sets the time in milliseconds from SIGTERM to SIGKILL, from 0 to 600000.
    /// When it is absent, the grace is 2000 ms.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? GraceMs { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'launch_id' is required."
        : null;

    void IIpcReusable.Reset()
    {
        _launchId = default;
        _present = 0;
        GraceMs = null;
    }
}
