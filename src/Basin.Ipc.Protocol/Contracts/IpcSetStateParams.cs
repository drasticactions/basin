using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>windows/set-state</c>. The <c>id</c> key is required, and each state key that is present asks for one change.
/// </summary>
public sealed class IpcSetStateParams : IIpcParams, IIpcReusable
{
    private ulong _id;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcSetStateParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcSetStateParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcSetStateParams"/> class.
    /// The <c>id</c> key is present. A <see langword="null"/> argument leaves its state unchanged.
    /// </summary>
    /// <param name="id">The window to change.</param>
    /// <param name="maximized">The maximized state to ask for.</param>
    /// <param name="minimized">The minimized state to ask for.</param>
    /// <param name="fullscreen">The fullscreen state to ask for.</param>
    /// <param name="noBorder">The borderless state to ask for.</param>
    public IpcSetStateParams(
        ulong id,
        bool? maximized = null,
        bool? minimized = null,
        bool? fullscreen = null,
        bool? noBorder = null)
    {
        Id = id;
        Maximized = maximized;
        Minimized = minimized;
        Fullscreen = fullscreen;
        NoBorder = noBorder;
    }

    /// <summary>
    /// Gets or sets the id of the window to change.
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
    /// Gets or sets a value indicating whether the window is to be maximized. Absent leaves the state unchanged.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Maximized { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window is to be minimized. Absent leaves the state unchanged.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Minimized { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window is to be fullscreen. Absent leaves the state unchanged.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Fullscreen { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window is to lose its server-side decorations. Absent leaves the state unchanged.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NoBorder { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _present = 0;
        Maximized = null;
        Minimized = null;
        Fullscreen = null;
        NoBorder = null;
    }
}
