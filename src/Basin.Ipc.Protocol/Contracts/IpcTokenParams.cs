using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>idle/uninhibit</c>. The <c>token</c> key is required.
/// </summary>
public sealed class IpcTokenParams : IIpcParams, IIpcReusable
{
    private string _token = string.Empty;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcTokenParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcTokenParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcTokenParams"/> class.
    /// The <c>token</c> key is present.
    /// </summary>
    /// <param name="token">The inhibitor token.</param>
    public IpcTokenParams(string token)
    {
        Token = token;
    }

    /// <summary>
    /// Gets or sets the token from <see cref="IpcIdleToken.Token"/> that names the inhibitor to release.
    /// An explicit JSON <see langword="null"/> does not count as present.
    /// </summary>
    public string Token
    {
        get => _token;
        set
        {
            _token = value;
            _present |= value is null ? 0 : 1;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'token' is required."
        : null;

    void IIpcReusable.Reset()
    {
        _token = string.Empty;
        _present = 0;
    }
}
