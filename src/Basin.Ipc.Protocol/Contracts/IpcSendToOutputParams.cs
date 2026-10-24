using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>windows/send-to-output</c>. The <c>id</c> and <c>output</c> keys are required.
/// </summary>
public sealed class IpcSendToOutputParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private string _output = string.Empty;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcSendToOutputParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcSendToOutputParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcSendToOutputParams"/> class.
    /// Both keys are present.
    /// </summary>
    /// <param name="id">The window to send.</param>
    /// <param name="output">The name of the target output.</param>
    public IpcSendToOutputParams(ulong id, string output)
    {
        Id = id;
        Output = output;
    }

    /// <summary>
    /// Gets or sets the id of the window to send.
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
    /// Gets or sets the name of the output that receives the window.
    /// An explicit JSON <see langword="null"/> does not count as present.
    /// </summary>
    public string Output
    {
        get => _output;
        set
        {
            _output = value;
            _present |= value is null ? 0 : 2;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'output' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _output = string.Empty;
        _present = 0;
    }
}
