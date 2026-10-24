using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>process/spawn</c>. The <c>argv</c> key is required. The child starts with no shell, in a new session.
/// </summary>
public sealed class IpcSpawnParams : IIpcParams, IIpcReusable
{
    private IReadOnlyList<string> _argv = [];

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcSpawnParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcSpawnParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcSpawnParams"/> class.
    /// The <c>argv</c> key is present. A <see langword="null"/> argument leaves its key out of the request.
    /// </summary>
    /// <param name="argv">The program and its arguments.</param>
    /// <param name="env">The variables to set.</param>
    /// <param name="cwd">The absolute working directory.</param>
    /// <param name="log">The absolute path of the log file.</param>
    /// <param name="unsetEnv">The variables to remove.</param>
    /// <param name="x11Density">The Xwayland density for the child.</param>
    public IpcSpawnParams(
        IReadOnlyList<string> argv,
        IReadOnlyDictionary<string,
        string>? env = null,
        string? cwd = null,
        string? log = null,
        IReadOnlyList<string>? unsetEnv = null,
        string? x11Density = null)
    {
        Argv = argv;
        Env = env;
        Cwd = cwd;
        Log = log;
        UnsetEnv = unsetEnv;
        X11Density = x11Density;
    }

    /// <summary>
    /// Gets or sets the argument vector. The first entry names the program, which the method finds on <c>PATH</c>.
    /// It must hold at least a program name. An explicit JSON <see langword="null"/> does not count as present.
    /// </summary>
    public IReadOnlyList<string> Argv
    {
        get => _argv;
        set
        {
            _argv = value;
            _present |= value is null ? 0 : 1;
        }
    }

    /// <summary>
    /// Gets or sets variables to add to the environment of the child, or to replace in it.
    /// Absent leaves the compositor's environment as it is.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Env { get; set; }

    /// <summary>
    /// Gets or sets the absolute working directory of the child. A relative path is <c>invalid_params</c>.
    /// When it is absent, the child starts in the compositor's working directory.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; set; }

    /// <summary>
    /// Gets or sets the absolute path of a file that receives the standard output and standard error of the child.
    /// The child appends to it. When it is absent, the child has no log, and <c>process/log</c> answers <c>not_found</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Log { get; set; }

    /// <summary>
    /// Gets or sets the names of variables to remove from the environment of the child.
    /// The removal happens after <see cref="Env"/> is applied.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? UnsetEnv { get; set; }

    /// <summary>
    /// Gets or sets the Xwayland server that the child connects to: <see cref="IpcWindow.NativeDensity"/> or <see cref="IpcWindow.LowDensity"/>.
    /// Any other value is <c>invalid_params</c>. Absent means the compositor's default.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? X11Density { get; set; }

    /// <inheritdoc/>
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
        X11Density = null;
    }
}
