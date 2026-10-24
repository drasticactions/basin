using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// One entry of <see cref="IpcOutputsParams.Entries"/>. It names one output and only the settings that change.
/// The <c>name</c> key is required.
/// </summary>
public sealed class IpcOutputChange : IIpcParams, IIpcReusable
{
    private string _name = string.Empty;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcOutputChange"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcOutputChange()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcOutputChange"/> class.
    /// The <c>name</c> key is present. A <see langword="null"/> argument leaves its setting unchanged.
    /// </summary>
    /// <param name="name">The output name.</param>
    /// <param name="enabled">The new enabled state.</param>
    /// <param name="mode">The new mode.</param>
    /// <param name="position">The new position in layout coordinates.</param>
    /// <param name="scale">The new scale.</param>
    /// <param name="transform">The new transform name.</param>
    /// <param name="adaptiveSync">The new adaptive sync state.</param>
    public IpcOutputChange(
        string name,
        bool? enabled = null,
        IpcModeChange? mode = null,
        IpcPoint? position = null,
        double? scale = null,
        string? transform = null,
        bool? adaptiveSync = null)
    {
        Name = name;
        Enabled = enabled;
        Mode = mode;
        Position = position;
        Scale = scale;
        Transform = transform;
        AdaptiveSync = adaptiveSync;
    }

    /// <summary>
    /// Gets or sets the name of the output to change.
    /// An explicit JSON <see langword="null"/> does not count as present.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _present |= value is null ? 0 : 1;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the output is on after the change.
    /// When it is absent, the output keeps its own state.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets the new mode, or <see langword="null"/> to keep the current mode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IpcModeChange? Mode { get; set; }

    /// <summary>
    /// Gets or sets the new top-left corner of the output in layout coordinates, or <see langword="null"/> to keep it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IpcPoint? Position { get; set; }

    /// <summary>
    /// Gets or sets the new scale factor, above 0 and at most 16, or <see langword="null"/> to keep it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Scale { get; set; }

    /// <summary>
    /// Gets or sets the new transform: <c>normal</c>, <c>90</c>, <c>180</c>, <c>270</c>, <c>flipped</c> or <c>flipped-90</c> to <c>flipped-270</c>.
    /// Absent keeps the current transform.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Transform { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether adaptive sync is on after the change.
    /// When it is absent, the output keeps its state.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AdaptiveSync { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'name' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _name = string.Empty;
        _present = 0;
        Enabled = null;
        Mode = null;
        Position = null;
        Scale = null;
        Transform = null;
        AdaptiveSync = null;
    }
}
