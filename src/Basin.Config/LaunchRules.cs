using Basin.Diagnostics;
using Basin.Freedesktop;
using Tomlyn.Model;

namespace Basin.Config;

/// <summary>
/// The <c>[[launch]]</c> rules of a configuration, in the order in which they are tried.
/// </summary>
public sealed class LaunchRules
{
    /// <summary>
    /// The name of the array of tables that holds the rules.
    /// </summary>
    public const string Table = "launch";

    private static readonly string[] Shells = ["sh", "bash", "dash", "zsh", "ksh", "fish"];

    private readonly LaunchRule[] _mostSpecificFirst;

    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchRules"/> class.
    /// The rules are sorted by <see cref="LaunchRule.Specificity"/>, highest first. Rules of equal rank keep their order.
    /// </summary>
    /// <param name="rules">The rules in file order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is <see langword="null"/>.</exception>
    public LaunchRules(IEnumerable<LaunchRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _mostSpecificFirst = [.. rules.OrderByDescending(static rule => rule.Specificity)];
    }

    /// <summary>
    /// Gets a set with no rules.
    /// </summary>
    public static LaunchRules Empty { get; } = new([]);

    /// <summary>
    /// Gets the rules, most specific first.
    /// </summary>
    public IReadOnlyList<LaunchRule> Rules => _mostSpecificFirst;

    /// <summary>
    /// Reads the <c>[[launch]]</c> rows from a configuration reader. A row with neither <c>desktop_id</c> nor <c>exec</c> is dropped with a warning.
    /// </summary>
    /// <param name="root">The reader of the root table.</param>
    /// <returns>The rules.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public static LaunchRules Read(TomlReader root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var rules = new List<LaunchRule>();
        foreach (var row in root.Sections(Table))
        {
            if (Parse(key => row.Raw(key), row.Where, row.Where(string.Empty).TrimEnd('.'), row.Log) is { } rule)
            {
                rules.Add(rule);
            }
        }

        return new(rules);
    }

    /// <summary>
    /// Reads rules from a parsed array of tables. It warns about unknown keys and values, and drops a row with neither <c>desktop_id</c> nor <c>exec</c>.
    /// </summary>
    /// <param name="rows">The <c>launch</c> array, or <see langword="null"/>.</param>
    /// <param name="log">The logger for warnings.</param>
    /// <returns>The rules, or <see cref="Empty"/> when <paramref name="rows"/> is <see langword="null"/>.</returns>
    public static LaunchRules Read(TomlTableArray? rows, BasinLogger log)
    {
        if (rows is null)
        {
            return Empty;
        }

        var rules = new List<LaunchRule>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var index = i;
            foreach (var (key, _) in row)
            {
                if (key is not ("desktop_id" or "exec" or "x11"))
                {
                    log.Warn($"unknown key '{Table}[{index}].{key}', ignored");
                }
            }

            if (Parse(key => row.TryGetValue(key, out var value) ? value : null, key => $"{Table}[{index}].{key}", $"{Table}[{index}]", log) is { } rule)
            {
                rules.Add(rule);
            }
        }

        return new(rules);
    }

    /// <summary>
    /// Reads the rules from a configuration file.
    /// </summary>
    /// <param name="path">The path of the TOML file.</param>
    /// <param name="log">The logger for parse warnings.</param>
    /// <returns>The rules, or <see cref="Empty"/> when the file is missing, does not parse or has no <c>launch</c> array.</returns>
    public static LaunchRules Load(string path, BasinLogger log) =>
        TomlConfig.Read(path, log) is { } table && table.TryGetValue(Table, out var rows) && rows is TomlTableArray array
            ? Read(array, log)
            : Empty;

    /// <summary>
    /// Finds the density of the most specific rule that matches the launch and sets a density.
    /// </summary>
    /// <param name="desktopId">The desktop entry ID of the launch, or <see langword="null"/>.</param>
    /// <param name="exec">The program of the launch, or <see langword="null"/>.</param>
    /// <returns>The density, or <see langword="null"/> when no rule matches. The caller then uses the native server.</returns>
    public X11Density? For(string? desktopId, string? exec)
    {
        foreach (var rule in _mostSpecificFirst)
        {
            if (rule.X11 is { } density && rule.Matches(desktopId, exec))
            {
                return density;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the density for a structured launch. The program comes from <see cref="ProgramOf(IReadOnlyList{string})"/>.
    /// </summary>
    /// <param name="desktopId">The desktop entry ID of the launch, or <see langword="null"/>.</param>
    /// <param name="argv">The arguments of the launch.</param>
    /// <returns>The density, or <see langword="null"/> when no rule matches.</returns>
    public X11Density? ForArgv(string? desktopId, IReadOnlyList<string> argv) => For(desktopId, ProgramOf(argv));

    /// <summary>
    /// Finds the density for a shell command string. The program comes from <see cref="ProgramOf(string)"/>.
    /// </summary>
    /// <param name="desktopId">The desktop entry ID of the launch, or <see langword="null"/>.</param>
    /// <param name="command">The command string.</param>
    /// <returns>The density, or <see langword="null"/> when no rule matches.</returns>
    public X11Density? ForCommand(string? desktopId, string command) => For(desktopId, ProgramOf(command));

    /// <summary>
    /// Finds the program that a shell command string runs. The string is split as a desktop entry Exec line.
    /// </summary>
    /// <param name="command">The command string.</param>
    /// <returns>The base name of the program, or <see langword="null"/> when the command has none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public static string? ProgramOf(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ProgramOf(ExecLine.Split(command));
    }

    /// <summary>
    /// Finds the program that an argument list runs. A leading <c>env</c> and <c>NAME=value</c> words are skipped. For a shell with <c>-c</c>, the command string is read instead.
    /// </summary>
    /// <param name="argv">The arguments.</param>
    /// <returns>The base name of the program, or <see langword="null"/> when the list has none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="argv"/> is <see langword="null"/>.</exception>
    public static string? ProgramOf(IReadOnlyList<string> argv)
    {
        ArgumentNullException.ThrowIfNull(argv);
        if (argv.Count >= 3 && Shells.Contains(Path.GetFileName(argv[0])) && argv[1] == "-c")
        {
            return ProgramOf(argv[2]);
        }

        var i = 0;
        if (i < argv.Count && Path.GetFileName(argv[i]) == "env")
        {
            i++;
        }

        while (i < argv.Count && IsAssignment(argv[i]))
        {
            i++;
        }

        return i < argv.Count ? Path.GetFileName(argv[i]) : null;
    }

    private static bool IsAssignment(string word)
    {
        var equals = word.IndexOf('=', StringComparison.Ordinal);
        if (equals <= 0)
        {
            return false;
        }

        for (var i = 0; i < equals; i++)
        {
            var c = word[i];
            if (!(c == '_' || char.IsAsciiLetterOrDigit(c)) || (i == 0 && char.IsAsciiDigit(c)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gives the configuration word for a density.
    /// </summary>
    /// <param name="density">The density.</param>
    /// <returns><c>low-density</c> for <see cref="X11Density.Low"/>, else <c>native</c>.</returns>
    public static string Name(X11Density density) => density == X11Density.Low ? "low-density" : "native";

    private static LaunchRule? Parse(Func<string, object?> take, Func<string, string> where, string row, BasinLogger log)
    {
        var desktopId = Text(take("desktop_id"), where("desktop_id"), log);
        var exec = Text(take("exec"), where("exec"), log);
        X11Density? x11 = take("x11") switch
        {
            null => null,
            "native" => X11Density.Native,
            "low-density" => X11Density.Low,
            var other => Warn(where("x11"), other, log),
        };
        if (desktopId is null && exec is null)
        {
            log.Warn($"{row} names neither desktop_id nor exec, dropped");
            return null;
        }

        return new LaunchRule { DesktopId = desktopId, Exec = exec, X11 = x11 };
    }

    private static X11Density? Warn(string where, object value, BasinLogger log)
    {
        log.Warn($"{where} \"{value}\" is not native or low-density, ignored");
        return null;
    }

    private static string? Text(object? value, string where, BasinLogger log)
    {
        switch (value)
        {
            case null:
                return null;
            case string { Length: > 0 } text:
                return text;
            default:
                log.Warn($"{where} \"{value}\" is not a non-empty string, ignored");
                return null;
        }
    }
}
