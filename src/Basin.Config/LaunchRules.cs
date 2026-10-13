using Basin.Diagnostics;
using Basin.Freedesktop;
using Tomlyn.Model;

namespace Basin.Config;

public sealed class LaunchRules
{
    public const string Table = "launch";

    private static readonly string[] Shells = ["sh", "bash", "dash", "zsh", "ksh", "fish"];

    private readonly LaunchRule[] _mostSpecificFirst;

    public LaunchRules(IEnumerable<LaunchRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _mostSpecificFirst = [.. rules.OrderByDescending(static rule => rule.Specificity)];
    }

    public static LaunchRules Empty { get; } = new([]);

    public IReadOnlyList<LaunchRule> Rules => _mostSpecificFirst;

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

    public static LaunchRules Load(string path, BasinLogger log) =>
        TomlConfig.Read(path, log) is { } table && table.TryGetValue(Table, out var rows) && rows is TomlTableArray array
            ? Read(array, log)
            : Empty;

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

    public X11Density? ForArgv(string? desktopId, IReadOnlyList<string> argv) => For(desktopId, ProgramOf(argv));

    public X11Density? ForCommand(string? desktopId, string command) => For(desktopId, ProgramOf(command));

    public static string? ProgramOf(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ProgramOf(ExecLine.Split(command));
    }

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
