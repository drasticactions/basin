using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>process/log</c>. The <c>launch_id</c> key is required.
/// </summary>
public sealed class IpcProcessLogParams : IIpcParams, IIpcReusable
{
    private long _launchId;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcProcessLogParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcProcessLogParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcProcessLogParams"/> class.
    /// The <c>launch_id</c> key is present.
    /// </summary>
    /// <param name="launchId">The launch to read.</param>
    /// <param name="lines">The count of lines, or <see langword="null"/> for the default.</param>
    public IpcProcessLogParams(long launchId, long? lines = null)
    {
        LaunchId = launchId;
        Lines = lines;
    }

    /// <summary>
    /// Gets or sets the launch id whose log the method reads.
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
    /// Gets or sets the count of lines to return from the end of the log, from 1 to 10000.
    /// When it is absent, the count is 50. The method reads at most the last 1 MiB of the file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Lines { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'launch_id' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _launchId = default;
        _present = 0;
        Lines = null;
    }
}
