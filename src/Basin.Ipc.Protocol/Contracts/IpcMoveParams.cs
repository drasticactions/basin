using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>windows/move</c>. The <c>id</c>, <c>x</c> and <c>y</c> keys are required.
/// </summary>
public sealed class IpcMoveParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private int _x;
    private int _y;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcMoveParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcMoveParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcMoveParams"/> class.
    /// Every key is present.
    /// </summary>
    /// <param name="id">The window to move.</param>
    /// <param name="x">The new left edge in layout coordinates.</param>
    /// <param name="y">The new top edge in layout coordinates.</param>
    public IpcMoveParams(ulong id, int x, int y)
    {
        Id = id;
        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets or sets the id of the window to move.
    /// JSON can carry it as a number or as a string of decimal digits.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Id
    {
        get => _id;
        set
        {
            _id = value;
            _present |= 1;
        }
    }

    /// <summary>
    /// Gets or sets the new left edge of the window in layout coordinates.
    /// </summary>
    public int X
    {
        get => _x;
        set
        {
            _x = value;
            _present |= 2;
        }
    }

    /// <summary>
    /// Gets or sets the new top edge of the window in layout coordinates.
    /// </summary>
    public int Y
    {
        get => _y;
        set
        {
            _y = value;
            _present |= 4;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'x' is required"
        : (_present & 4) == 0 ? "'y' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _x = default;
        _y = default;
        _present = 0;
    }
}
