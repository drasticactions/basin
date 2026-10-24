using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>windows/wait-idle</c>. Every key is optional.
/// </summary>
public sealed class IpcWaitIdleParams : IIpcParams, IIpcReusable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpcWaitIdleParams"/> class.
    /// Every key starts absent.
    /// </summary>
    public IpcWaitIdleParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcWaitIdleParams"/> class.
    /// A <see langword="null"/> argument leaves its key out of the request.
    /// </summary>
    /// <param name="id">The window to watch, or <see langword="null"/> for every window.</param>
    /// <param name="quietMs">The quiet time in milliseconds.</param>
    /// <param name="ignoreBelow">The damage size in logical pixels that does not count.</param>
    /// <param name="timeoutMs">The time limit in milliseconds.</param>
    public IpcWaitIdleParams(ulong? id = null, long? quietMs = null, long? ignoreBelow = null, long? timeoutMs = null)
    {
        Id = id;
        QuietMs = quietMs;
        IgnoreBelow = ignoreBelow;
        TimeoutMs = timeoutMs;
    }

    /// <summary>
    /// Gets or sets the id of the window to watch. When it is absent, the method waits for every window.
    /// JSON can carry it as a number or as a string of decimal digits.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Id { get; set; }

    /// <summary>
    /// Gets or sets the time with no counting commit, in milliseconds, after which the window is idle.
    /// It is from 1 to 3600000. When it is absent, it is 300 ms.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? QuietMs { get; set; }

    /// <summary>
    /// Gets or sets the side of a square in logical pixels. A commit whose damage fits in the square does not count.
    /// It is from 0 to 65535. When it is absent, it is 64.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? IgnoreBelow { get; set; }

    /// <summary>
    /// Gets or sets the time in milliseconds after which the method answers <c>failed</c>, from 1 to 3600000.
    /// When it is absent, the limit is 5000 ms.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TimeoutMs { get; set; }

    /// <summary>
    /// Gets <see langword="null"/> at all times, because this request has no required key.
    /// </summary>
    [JsonIgnore]
    public string? Missing => null;

    void IIpcReusable.Reset()
    {
        Id = null;
        QuietMs = null;
        IgnoreBelow = null;
        TimeoutMs = null;
    }
}
