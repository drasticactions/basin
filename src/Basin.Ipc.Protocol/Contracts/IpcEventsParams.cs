using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>ipc/subscribe</c> and <c>ipc/unsubscribe</c>.
/// </summary>
public sealed class IpcEventsParams : IIpcParams, IIpcReusable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpcEventsParams"/> class. Every key starts absent.
    /// </summary>
    public IpcEventsParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcEventsParams"/> class.
    /// </summary>
    /// <param name="events">The event names, or null.</param>
    public IpcEventsParams(IReadOnlyList<string>? events = null)
    {
        Events = events;
    }

    /// <summary>
    /// Gets or sets the event names. <c>ipc/subscribe</c> needs them. For <c>ipc/unsubscribe</c>, <see langword="null"/> ends every subscription of the connection.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Events { get; set; }

    /// <summary>
    /// Gets or sets the options for each event, keyed by event name, for example <c>{"diag/input": {"motion": true}}</c>. <see langword="null"/> means no options.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, System.Text.Json.JsonElement>? Options { get; set; }

    /// <summary>
    /// Gets <see langword="null"/> always. The handler of <c>ipc/subscribe</c> checks <see cref="Events"/> itself.
    /// </summary>
    [JsonIgnore]
    public string? Missing => null;

    void IIpcReusable.Reset()
    {
        Events = null;
        Options = null;
    }
}
