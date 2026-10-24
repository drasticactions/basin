using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>outputs/test</c> and <c>outputs/apply</c>. The <c>entries</c> key is required.
/// </summary>
public sealed class IpcOutputsParams : IIpcParams, IIpcReusable
{
    private IReadOnlyList<IpcOutputChange> _entries = [];

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcOutputsParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcOutputsParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcOutputsParams"/> class.
    /// The <c>entries</c> key is present.
    /// </summary>
    /// <param name="entries">One change for each output that changes.</param>
    public IpcOutputsParams(IReadOnlyList<IpcOutputChange> entries)
    {
        Entries = entries;
    }

    /// <summary>
    /// Gets or sets one change for each output that changes. An output with no entry keeps its configuration.
    /// An explicit JSON <see langword="null"/> does not count as present.
    /// </summary>
    public IReadOnlyList<IpcOutputChange> Entries
    {
        get => _entries;
        set
        {
            _entries = value;
            _present |= value is null ? 0 : 1;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'entries' is required."
        : null;

    void IIpcReusable.Reset()
    {
        _entries = [];
        _present = 0;
    }
}
