using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>windows/set-x11-density</c>. The <c>id</c> and <c>density</c> keys are required.
/// </summary>
public sealed class IpcSetX11DensityParams : IIpcParams, IIpcReusable
{
    private ulong _id;

    private string _density = string.Empty;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcSetX11DensityParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcSetX11DensityParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcSetX11DensityParams"/> class.
    /// Both keys are present.
    /// </summary>
    /// <param name="id">The X11 window to move.</param>
    /// <param name="density">The target density name.</param>
    public IpcSetX11DensityParams(ulong id, string density)
    {
        Id = id;
        Density = density;
    }

    /// <summary>
    /// Gets or sets the id of the X11 window whose application moves to another density.
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
    /// Gets or sets the target density: <see cref="IpcWindow.NativeDensity"/> or <see cref="IpcWindow.LowDensity"/>.
    /// An explicit JSON <see langword="null"/> does not count as present.
    /// </summary>
    public string Density
    {
        get => _density;
        set
        {
            _density = value;
            _present |= value is null ? 0 : 2;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'density' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _density = string.Empty;
        _present = 0;
    }
}
