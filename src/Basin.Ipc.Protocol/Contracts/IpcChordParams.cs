using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>input/chord</c> and <c>seat/chord</c>.
/// </summary>
public sealed class IpcChordParams : IIpcParams, IIpcReusable
{
    private string _chord = string.Empty;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcChordParams"/> class. Every key starts absent, so the deserializer can fill it.
    /// </summary>
    public IpcChordParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcChordParams"/> class.
    /// </summary>
    /// <param name="chord">The chord text.</param>
    public IpcChordParams(string chord)
    {
        Chord = chord;
    }

    /// <summary>
    /// Gets or sets the chord in the <c>Mod+Mod+Key</c> grammar, for example <c>Alt+n</c>. A bare modifier presses its left key. The key is required.
    /// </summary>
    public string Chord
    {
        get => _chord;
        set
        {
            _chord = value;
            _present |= value is null ? 0 : 1;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'chord' is required."
        : null;

    void IIpcReusable.Reset()
    {
        _chord = string.Empty;
        _present = 0;
    }
}
