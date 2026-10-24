using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>windows/send-to-workspace</c>. The <c>id</c> and <c>workspace</c> keys are required.
/// </summary>
public sealed class IpcSendToWorkspaceParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private ulong _workspace;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcSendToWorkspaceParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcSendToWorkspaceParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcSendToWorkspaceParams"/> class.
    /// Both keys are present.
    /// </summary>
    /// <param name="id">The window to send.</param>
    /// <param name="workspace">The id of the target workspace.</param>
    public IpcSendToWorkspaceParams(ulong id, ulong workspace)
    {
        Id = id;
        Workspace = workspace;
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
    /// Gets or sets the id of the workspace that receives the window, from <see cref="IpcWorkspace.Id"/>.
    /// JSON can carry it as a number or as a string of decimal digits.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Workspace
    {
        get => _workspace;
        set
        {
            _workspace = value;
            _present |= 2;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'workspace' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _workspace = default;
        _present = 0;
    }
}
