using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>input/pointer-button</c> and <c>seat/pointer-button</c>. The <c>button</c> key is required.
/// </summary>
public sealed class IpcPointerButtonParams : IIpcParams, IIpcReusable
{
    private IpcButton _button;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcPointerButtonParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcPointerButtonParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcPointerButtonParams"/> class.
    /// The <c>button</c> key is present.
    /// </summary>
    /// <param name="button">The button to send.</param>
    /// <param name="pressed">The button state, or <see langword="null"/> for a press and a release.</param>
    public IpcPointerButtonParams(IpcButton button, bool? pressed = null)
    {
        Button = button;
        Pressed = pressed;
    }

    /// <summary>
    /// Gets or sets the button. On the wire it is a name such as <c>left</c>, or an evdev button code.
    /// </summary>
    public IpcButton Button
    {
        get => _button;
        set
        {
            _button = value;
            _present |= 1;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the button goes down or up.
    /// When it is absent, the method sends a press and then a release.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Pressed { get; set; }

    /// <summary>
    /// Gets or sets the horizontal position to move the pointer to before the click.
    /// It is in layout coordinates, or relative to the client area of <see cref="Window"/> when that is set.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? X { get; set; }

    /// <summary>
    /// Gets or sets the vertical position to move the pointer to before the click.
    /// It is in layout coordinates, or relative to the client area of <see cref="Window"/> when that is set.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Y { get; set; }

    /// <summary>
    /// Gets or sets the id of the window that <see cref="X"/> and <see cref="Y"/> are relative to.
    /// The call is refused when another window covers the point. Absent means layout coordinates.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Window { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the method activates <see cref="Window"/> when another window covers the point.
    /// Absent means no activation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Raise { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'button' is required"
        : null;

    void IIpcReusable.Reset()
    {
        Window = null;
        Raise = null;
        X = null;
        Y = null;
        _button = default;
        _present = 0;
        Pressed = null;
    }
}
