using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>windows/resize</c>. The <c>id</c>, <c>width</c> and <c>height</c> keys are required.
/// </summary>
public sealed class IpcResizeParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private int _width;
    private int _height;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcResizeParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcResizeParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcResizeParams"/> class.
    /// Every key is present.
    /// </summary>
    /// <param name="id">The window to resize.</param>
    /// <param name="width">The new width in logical pixels.</param>
    /// <param name="height">The new height in logical pixels.</param>
    public IpcResizeParams(ulong id, int width, int height)
    {
        Id = id;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets or sets the id of the window to resize.
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
    /// Gets or sets the new width of the window in logical pixels.
    /// </summary>
    public int Width
    {
        get => _width;
        set
        {
            _width = value;
            _present |= 2;
        }
    }

    /// <summary>
    /// Gets or sets the new height of the window in logical pixels.
    /// </summary>
    public int Height
    {
        get => _height;
        set
        {
            _height = value;
            _present |= 4;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required."
        : (_present & 2) == 0 ? "'width' is required."
        : (_present & 4) == 0 ? "'height' is required."
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _width = default;
        _height = default;
        _present = 0;
    }
}
