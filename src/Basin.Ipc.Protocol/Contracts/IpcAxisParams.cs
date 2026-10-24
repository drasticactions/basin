using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>input/axis</c> and <c>seat/axis</c>: one scroll event, optionally at a point.
/// </summary>
public sealed class IpcAxisParams : IIpcParams, IIpcReusable
{
    private double _value;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcAxisParams"/> class. Every key starts absent, so the deserializer can fill it.
    /// </summary>
    public IpcAxisParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcAxisParams"/> class.
    /// </summary>
    /// <param name="value">The scroll amount.</param>
    /// <param name="axis"><c>vertical</c> or <c>horizontal</c>, or null for vertical.</param>
    /// <param name="source">The axis source word, or null for <c>wheel</c>.</param>
    public IpcAxisParams(double value, string? axis = null, string? source = null)
    {
        Value = value;
        Axis = axis;
        Source = source;
    }

    /// <summary>
    /// Gets or sets the scroll amount in the units of a <c>wl_pointer.axis</c> value. A positive value scrolls down or right. The key is required.
    /// </summary>
    public double Value
    {
        get => _value;
        set
        {
            _value = value;
            _present |= 1;
        }
    }

    /// <summary>
    /// Gets or sets the axis: <c>vertical</c> or <c>horizontal</c>. <see langword="null"/> means vertical.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Axis { get; set; }

    /// <summary>
    /// Gets or sets the axis source: <c>wheel</c>, <c>finger</c>, <c>continuous</c> or <c>wheel-tilt</c>. <see langword="null"/> means <c>wheel</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets the x of the point, relative to the client area of <see cref="Window"/>, or in layout coordinates without it. It goes with <see cref="Y"/>. <see langword="null"/> scrolls at the current position.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? X { get; set; }

    /// <summary>
    /// Gets or sets the y of the point, in the same space as <see cref="X"/>. <see langword="null"/> scrolls at the current position.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Y { get; set; }

    /// <summary>
    /// Gets or sets the id of the window that <see cref="X"/> and <see cref="Y"/> are relative to. A string of decimal digits is read too. <see langword="null"/> means layout coordinates.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Window { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to activate <see cref="Window"/> first when another window covers the point. <see langword="null"/> means no.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Raise { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'value' is required"
        : null;

    void IIpcReusable.Reset()
    {
        Window = null;
        Raise = null;
        X = null;
        Y = null;
        _value = default;
        _present = 0;
        Axis = null;
        Source = null;
    }
}
