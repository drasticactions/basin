using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>outputs/look</c>. The <c>output</c> key is required.
/// </summary>
public sealed class IpcLookParams : IIpcParams, IIpcReusable
{
    private string _output = string.Empty;
    private string? _path;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcLookParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcLookParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcLookParams"/> class.
    /// The <c>output</c> key is present.
    /// </summary>
    /// <param name="output">The output name.</param>
    /// <param name="path">The path of a <c>.cube</c> file, or <see langword="null"/> to remove the look.</param>
    public IpcLookParams(string output, string? path)
    {
        Output = output;
        Path = path;
    }

    /// <summary>
    /// Gets or sets the name of the output that the look applies to.
    /// An explicit JSON <see langword="null"/> does not count as present.
    /// </summary>
    public string Output
    {
        get => _output;
        set
        {
            _output = value;
            _present |= value is null ? 0 : 1;
        }
    }

    /// <summary>
    /// Gets or sets the path of the <c>.cube</c> file to apply.
    /// A <see langword="null"/> or empty path removes the look from the output.
    /// </summary>
    public string? Path
    {
        get => _path;
        set => _path = value;
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing => (_present & 1) == 0 ? "'output' is required" : null;

    void IIpcReusable.Reset()
    {
        _output = string.Empty;
        _path = null;
        _present = 0;
    }
}
