using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>capture/output</c>.
/// </summary>
public sealed class IpcCaptureOutputParams : IIpcParams, IIpcReusable
{
    private IpcCaptureTo _to;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcCaptureOutputParams"/> class. Every key starts absent, so the deserializer can fill it.
    /// </summary>
    public IpcCaptureOutputParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcCaptureOutputParams"/> class.
    /// </summary>
    /// <param name="to">The capture target.</param>
    /// <param name="output">The output name, or null.</param>
    /// <param name="cursor">Whether to overlay the cursor, or null.</param>
    /// <param name="scale">The downscale factor, or null.</param>
    /// <param name="maxDimension">The longest side in pixels, or null.</param>
    /// <param name="format"><c>png</c>, <c>png16</c> or null.</param>
    public IpcCaptureOutputParams(
        IpcCaptureTo to,
        string? output = null,
        bool? cursor = null,
        double? scale = null,
        long? maxDimension = null,
        string? format = null)
    {
        To = to;
        Output = output;
        Cursor = cursor;
        Scale = scale;
        MaxDimension = maxDimension;
        Format = format;
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
            _present |= 1;
        }
    }

    /// <summary>
    /// Gets or sets the output name. <see langword="null"/> means the output under the pointer, or the first output.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Output { get; set; }

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
        (_present & 1) == 0 ? "'to' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _to = default;
        _present = 0;
        Output = null;
        Cursor = null;
        Scale = null;
        MaxDimension = null;
        Format = null;
    }
}
