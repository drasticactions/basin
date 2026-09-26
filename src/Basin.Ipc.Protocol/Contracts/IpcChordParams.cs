using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcChordParams : IIpcParams, IIpcReusable
{
    private string _chord = string.Empty;

    private int _present;

    public IpcChordParams()
    {
    }

    public IpcChordParams(string chord)
    {
        Chord = chord;
    }

    public string Chord
    {
        get => _chord;
        set
        {
            _chord = value;
            _present |= value is null ? 0 : 1;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'chord' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _chord = string.Empty;
        _present = 0;
    }
}
