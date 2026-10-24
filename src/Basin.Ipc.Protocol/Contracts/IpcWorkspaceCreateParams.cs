using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>workspaces/create</c>. The <c>group</c> and <c>name</c> keys are required.
/// </summary>
public sealed class IpcWorkspaceCreateParams : IIpcParams, IIpcReusable
{
    private ulong _group;
    private string _name = string.Empty;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcWorkspaceCreateParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcWorkspaceCreateParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcWorkspaceCreateParams"/> class.
    /// Both keys are present.
    /// </summary>
    /// <param name="group">The group that receives the workspace.</param>
    /// <param name="name">The name of the new workspace.</param>
    public IpcWorkspaceCreateParams(ulong group, string name)
    {
        Group = group;
        Name = name;
    }

    /// <summary>
    /// Gets or sets the id of the workspace group, from <see cref="IpcWorkspaceGroup.Id"/>.
    /// JSON can carry it as a number or as a string of decimal digits.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Group
    {
        get => _group;
        set
        {
            _group = value;
            _present |= 1;
        }
    }

    /// <summary>
    /// Gets or sets the name of the new workspace.
    /// An explicit JSON <see langword="null"/> does not count as present.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _present |= value is null ? 0 : 2;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'group' is required"
        : (_present & 2) == 0 ? "'name' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _group = default;
        _name = string.Empty;
        _present = 0;
    }
}
