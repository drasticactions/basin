using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>input/pointer-move</c> and <c>seat/pointer-move</c>. The <c>x</c> and <c>y</c> keys are required.
/// </summary>
public sealed class IpcPointerMoveParams : IIpcParams, IIpcReusable
{
    private double _x;
    private double _y;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcPointerMoveParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcPointerMoveParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcPointerMoveParams"/> class.
    /// The <c>x</c> and <c>y</c> keys are present.
    /// </summary>
    /// <param name="x">The horizontal target position.</param>
    /// <param name="y">The vertical target position.</param>
    public IpcPointerMoveParams(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets or sets the horizontal target position.
    /// It is in layout coordinates, or relative to the client area of <see cref="Window"/> when that is set.
    /// </summary>
    public double X
    {
        get => _x;
        set
        {
            _x = value;
            _present |= 1;
        }
    }

    /// <summary>
    /// Gets or sets the vertical target position.
    /// It is in layout coordinates, or relative to the client area of <see cref="Window"/> when that is set.
    /// </summary>
    public double Y
    {
        get => _y;
        set
        {
            _y = value;
            _present |= 2;
        }
    }

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
        (_present & 1) == 0 ? "'x' is required."
        : (_present & 2) == 0 ? "'y' is required."
        : null;

    void IIpcReusable.Reset()
    {
        Window = null;
        Raise = null;
        _x = default;
        _y = default;
        _present = 0;
    }
}
