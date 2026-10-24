using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>capture/region</c>: a box in layout coordinates.
/// </summary>
public sealed class IpcCaptureRegionParams : IIpcParams, IIpcReusable
{
    private int _x;
    private int _y;
    private int _width;
    private int _height;
    private IpcCaptureTo _to;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcCaptureRegionParams"/> class. Every key starts absent, so the deserializer can fill it.
    /// </summary>
    public IpcCaptureRegionParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcCaptureRegionParams"/> class.
    /// </summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="to">The capture target.</param>
    /// <param name="cursor">Whether to overlay the cursor, or null.</param>
    /// <param name="scale">The downscale factor, or null.</param>
    /// <param name="maxDimension">The longest side in pixels, or null.</param>
    /// <param name="format"><c>png</c>, <c>png16</c> or null.</param>
    public IpcCaptureRegionParams(
        int x,
        int y,
        int width,
        int height,
        IpcCaptureTo to,
        bool? cursor = null,
        double? scale = null,
        long? maxDimension = null,
        string? format = null)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        To = to;
        Cursor = cursor;
        Scale = scale;
        MaxDimension = maxDimension;
        Format = format;
    }

    /// <summary>
    /// Gets or sets the left edge of the box in layout coordinates. The key is required.
    /// </summary>
    public int X
    {
        get => _x;
        set
        {
            _x = value;
            _present |= 1;
        }
    }

    /// <summary>
    /// Gets or sets the top edge of the box in layout coordinates. The key is required.
    /// </summary>
    public int Y
    {
        get => _y;
        set
        {
            _y = value;
            _present |= 2;
        }
    }

    /// <summary>
    /// Gets or sets the width of the box in logical pixels, from 1 to 32768. The key is required.
    /// </summary>
    public int Width
    {
        get => _width;
        set
        {
            _width = value;
            _present |= 4;
        }
    }

    /// <summary>
    /// Gets or sets the height of the box in logical pixels, from 1 to 32768. The key is required.
    /// </summary>
    public int Height
    {
        get => _height;
        set
        {
            _height = value;
            _present |= 8;
        }
    }

    /// <summary>
    /// Gets or sets where the capture goes: a PNG file at an absolute path, an inline base64 PNG, or a descriptor of raw pixels. The key is required.
    /// </summary>
    public IpcCaptureTo To
    {
        get => _to;
        set
        {
            _to = value;
            _present |= 16;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the capture overlays the cursor. <see langword="null"/> means no cursor.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Cursor { get; set; }

    /// <summary>
    /// Gets or sets the downscale factor, above 0 and at most 1, applied by area average before the encoding. <see langword="null"/> means 1. It cannot go with <see cref="MaxDimension"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Scale { get; set; }

    /// <summary>
    /// Gets or sets the longest side of the image in pixels, at least 1. The capture scales down to it and never up. <see langword="null"/> means no limit.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxDimension { get; set; }

    /// <summary>
    /// Gets or sets the image format: <c>png</c> for 8-bit SDR or <c>png16</c> for the code values of the output. <see langword="null"/> means <c>png</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'x' is required."
        : (_present & 2) == 0 ? "'y' is required."
        : (_present & 4) == 0 ? "'width' is required."
        : (_present & 8) == 0 ? "'height' is required."
        : (_present & 16) == 0 ? "'to' is required."
        : null;

    void IIpcReusable.Reset()
    {
        _x = default;
        _y = default;
        _width = default;
        _height = default;
        _to = default;
        _present = 0;
        Cursor = null;
        Scale = null;
        MaxDimension = null;
        Format = null;
    }
}
