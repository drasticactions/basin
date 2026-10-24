using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The <c>mode</c> object of an <see cref="IpcOutputChange"/>. The <c>width</c> and <c>height</c> keys are required.
/// </summary>
public sealed class IpcModeChange : IIpcParams, IIpcReusable
{
    private int _width;
    private int _height;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcModeChange"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcModeChange()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcModeChange"/> class.
    /// The <c>width</c> and <c>height</c> keys are present.
    /// </summary>
    /// <param name="width">The mode width in pixels.</param>
    /// <param name="height">The mode height in pixels.</param>
    /// <param name="refreshMhz">The refresh rate in millihertz, or <see langword="null"/> to keep the current rate.</param>
    public IpcModeChange(int width, int height, int? refreshMhz = null)
    {
        Width = width;
        Height = height;
        RefreshMhz = refreshMhz;
    }

    /// <summary>
    /// Gets or sets the width of the mode in physical pixels.
    /// </summary>
    public int Width
    {
        get => _width;
        set
        {
            _width = value;
            _present |= 1;
        }
    }

    /// <summary>
    /// Gets or sets the height of the mode in physical pixels.
    /// </summary>
    public int Height
    {
        get => _height;
        set
        {
            _height = value;
            _present |= 2;
        }
    }

    /// <summary>
    /// Gets or sets the refresh rate in millihertz, for example 60000 for 60 Hz.
    /// When it is absent, the output keeps its current rate.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RefreshMhz { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'width' is required."
        : (_present & 2) == 0 ? "'height' is required."
        : null;

    void IIpcReusable.Reset()
    {
        _width = default;
        _height = default;
        _present = 0;
        RefreshMhz = null;
    }
}
