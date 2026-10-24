using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>input/touch</c> and <c>seat/touch</c>. The <c>kind</c> key is required.
/// The method also reads an optional <c>time_ms</c>, the event time on the monotonic clock in milliseconds.
/// </summary>
public sealed class IpcTouchParams : IIpcParams, IIpcReusable
{
    private string _kind = string.Empty;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcTouchParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcTouchParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcTouchParams"/> class.
    /// The <c>kind</c> key is present. A <see langword="null"/> argument leaves its key out of the request.
    /// </summary>
    /// <param name="kind">The touch event kind.</param>
    /// <param name="id">The touch point id.</param>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    public IpcTouchParams(string kind, int? id = null, double? x = null, double? y = null)
    {
        Kind = kind;
        Id = id;
        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets or sets the event kind: <c>down</c>, <c>motion</c>, <c>up</c>, <c>frame</c> or <c>cancel</c>.
    /// Any other value is <c>invalid_params</c>. An explicit JSON <see langword="null"/> does not count as present.
    /// </summary>
    public string Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            _present |= value is null ? 0 : 1;
        }
    }

    /// <summary>
    /// Gets or sets the id of the touch point. It pairs a <c>down</c> with its <c>motion</c> and <c>up</c>. Absent means 0.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Id { get; set; }

    /// <summary>
    /// Gets or sets the horizontal position in layout coordinates.
    /// A <c>down</c> or a <c>motion</c> must name it. The other kinds ignore it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? X { get; set; }

    /// <summary>
    /// Gets or sets the vertical position in layout coordinates.
    /// A <c>down</c> or a <c>motion</c> must name it. The other kinds ignore it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Y { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'kind' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _kind = string.Empty;
        _present = 0;
        Id = null;
        X = null;
        Y = null;
    }
}
