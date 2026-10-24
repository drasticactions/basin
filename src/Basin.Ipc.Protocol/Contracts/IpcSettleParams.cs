using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>session/settle</c>. Every key is optional.
/// </summary>
public sealed class IpcSettleParams : IIpcParams, IIpcReusable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpcSettleParams"/> class.
    /// Every key starts absent.
    /// </summary>
    public IpcSettleParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcSettleParams"/> class.
    /// A <see langword="null"/> argument leaves its key out of the request.
    /// </summary>
    /// <param name="timeoutMs">The time limit in milliseconds, or <see langword="null"/> for the default.</param>
    public IpcSettleParams(long? timeoutMs)
    {
        TimeoutMs = timeoutMs;
    }

    /// <summary>
    /// Gets or sets the time in milliseconds after which the method answers <c>failed</c>.
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
        TimeoutMs = null;
    }
}
