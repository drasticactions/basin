using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>input/key</c> and <c>seat/key</c>. The <c>code</c> key is required.
/// </summary>
public sealed class IpcKeyParams : IIpcParams, IIpcReusable
{
    private int _code;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcKeyParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcKeyParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcKeyParams"/> class.
    /// The <c>code</c> key is present.
    /// </summary>
    /// <param name="code">The evdev key code.</param>
    /// <param name="pressed">The key state, or <see langword="null"/> for a press and a release.</param>
    public IpcKeyParams(int code, bool? pressed = null)
    {
        Code = code;
        Pressed = pressed;
    }

    /// <summary>
    /// Gets or sets the evdev key code, as the Linux input event codes number it.
    /// It is not an XKB keycode, which is 8 higher.
    /// </summary>
    public int Code
    {
        get => _code;
        set
        {
            _code = value;
            _present |= 1;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the key goes down or up.
    /// When it is absent, the method sends a press and then a release.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Pressed { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'code' is required."
        : null;

    void IIpcReusable.Reset()
    {
        _code = default;
        _present = 0;
        Pressed = null;
    }
}
