using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcSpawnParams : IIpcParams, IIpcReusable
{
    private IReadOnlyList<string> _argv = [];

    private int _present;

    public IpcSpawnParams()
    {
    }

    public IpcSpawnParams(
        IReadOnlyList<string> argv,
        IReadOnlyDictionary<string,
        string>? env = null,
        string? cwd = null,
        string? log = null,
        IReadOnlyList<string>? unsetEnv = null)
    {
        Argv = argv;
        Env = env;
        Cwd = cwd;
        Log = log;
        UnsetEnv = unsetEnv;
    }

    public IReadOnlyList<string> Argv
    {
        get => _argv;
        set
        {
            _argv = value;
            _present |= value is null ? 0 : 1;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Env { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Log { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? UnsetEnv { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'argv' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _argv = [];
        _present = 0;
        Env = null;
        Cwd = null;
        Log = null;
        UnsetEnv = null;
    }
}
