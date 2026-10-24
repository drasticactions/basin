using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>outputs/power</c>. The <c>output</c> and <c>on</c> keys are required.
/// </summary>
public sealed class IpcPowerParams : IIpcParams, IIpcReusable
{
    private string _output = string.Empty;
    private bool _on;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcPowerParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcPowerParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcPowerParams"/> class.
    /// Both keys are present.
    /// </summary>
    /// <param name="output">The output name.</param>
    /// <param name="on">The power state to set.</param>
    public IpcPowerParams(string output, bool on)
    {
        Output = output;
        On = on;
    }

    /// <summary>
    /// Gets or sets the name of the output to turn on or off.
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
    /// Gets or sets a value indicating whether the output turns on. <see langword="false"/> turns its display power off.
    /// </summary>
    public bool On
    {
        get => _on;
        set
        {
            _on = value;
            _present |= 2;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'output' is required"
        : (_present & 2) == 0 ? "'on' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _output = string.Empty;
        _on = default;
        _present = 0;
    }
}
