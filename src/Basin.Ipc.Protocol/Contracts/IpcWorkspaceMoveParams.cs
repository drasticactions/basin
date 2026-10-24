using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>workspaces/move</c>. The <c>id</c> and <c>group</c> keys are required.
/// </summary>
public sealed class IpcWorkspaceMoveParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private ulong _group;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcWorkspaceMoveParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcWorkspaceMoveParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcWorkspaceMoveParams"/> class.
    /// Both keys are present.
    /// </summary>
    /// <param name="id">The workspace to move.</param>
    /// <param name="group">The group that receives it.</param>
    public IpcWorkspaceMoveParams(ulong id, ulong group)
    {
        Id = id;
        Group = group;
    }

    /// <summary>
    /// Gets or sets the id of the workspace to move.
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
    /// Gets or sets the id of the group that receives the workspace.
    /// JSON can carry it as a number or as a string of decimal digits.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Group
    {
        get => _group;
        set
        {
            _group = value;
            _present |= 2;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'group' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _group = default;
        _present = 0;
    }
}
