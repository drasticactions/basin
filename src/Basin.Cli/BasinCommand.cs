using System.Collections;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Globalization;
using Basin.Diagnostics;

namespace Basin.Cli;

/// <summary>
/// Wraps the root command of a basin program. It adds the shared <c>--help</c>, <c>--log-level</c> and <c>--alloc-report</c> options and reports the parsed options.
/// </summary>
public sealed class BasinCommand
{
    private readonly List<Func<ParseResult, string>> _report = [];
    private readonly List<Action<ParseResult>> _prepare = [];
    private readonly HelpOption _help = new("--help", "-h");
    private readonly Option<string> _logLevel;
    private readonly Option<bool> _allocReport;
    private long _frames;

    /// <summary>
    /// Initializes a new instance of the <see cref="BasinCommand"/> class. It replaces the default help option and adds <c>--log-level</c> and <c>--alloc-report</c>.
    /// </summary>
    /// <param name="description">The program description that <c>--help</c> shows.</param>
    public BasinCommand(string description)
    {
        Command = new RootCommand(description);

        for (var i = Command.Options.Count - 1; i >= 0; i--)
        {
            if (Command.Options[i] is HelpOption)
            {
                Command.Options.RemoveAt(i);
            }
        }

        Command.Options.Add(_help);
        _logLevel = Add(CommonOptions.LogLevel());
        _allocReport = Add(CommonOptions.AllocReport());
    }

    /// <summary>
    /// Gets the root command. A program can add subcommands and arguments to it.
    /// </summary>
    public RootCommand Command { get; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Run"/> writes the <c>OPTIONS</c> line before the body runs. The line is written only in a Debug build.
    /// </summary>
    public bool ReportsOptions { get; set; } = true;

    internal Option<string>? IpcOption { get; set; }

    /// <summary>
    /// Adds an option to the root command.
    /// </summary>
    /// <typeparam name="T">The value type of the option.</typeparam>
    /// <param name="option">The option to add.</param>
    /// <param name="report">Whether the <c>OPTIONS</c> line includes the option's parsed value, keyed by its name without leading dashes.</param>
    /// <returns>The same <paramref name="option"/>, so a caller can keep it to read the value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="option"/> is <see langword="null"/>.</exception>
    public Option<T> Add<T>(Option<T> option, bool report = true)
    {
        ArgumentNullException.ThrowIfNull(option);
        Command.Options.Add(option);
        if (report)
        {
            var key = option.Name.TrimStart('-');
            _report.Add(result => $"{key}={Format(result.GetValue(option))}");
        }

        return option;
    }

    /// <summary>
    /// Formats one <c>key=value</c> entry for the <c>OPTIONS</c> line. A null or empty value is <c>none</c>, a bool is <c>on</c> or <c>off</c>, and a list is comma-separated.
    /// </summary>
    /// <param name="key">The entry name.</param>
    /// <param name="value">The value to format with the invariant culture.</param>
    /// <returns>The formatted entry.</returns>
    public static string Report(string key, object? value) => $"{key}={Format(value)}";

    /// <summary>
    /// Adds an entry to the <c>OPTIONS</c> line that the caller formats from the parse result.
    /// </summary>
    /// <param name="entry">The function that returns the entry text, usually from <see cref="Report"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    public void AddReport(Func<ParseResult, string> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _report.Add(entry);
    }

    /// <summary>
    /// Adds a step that runs after parsing and before the <c>OPTIONS</c> line and the body. Steps run in the order that they were added.
    /// </summary>
    /// <param name="step">The step to run with the parse result.</param>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is <see langword="null"/>.</exception>
    public void Prepare(Action<ParseResult> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _prepare.Add(step);
    }

    /// <summary>
    /// Gets the value that wins between the command line and the configuration file. The configuration value wins when the option was not given.
    /// </summary>
    /// <typeparam name="T">The value type of the option.</typeparam>
    /// <param name="result">The parse result.</param>
    /// <param name="option">The option to read.</param>
    /// <param name="fromConfig">The value from the configuration file.</param>
    /// <returns>The given option value, otherwise <paramref name="fromConfig"/>.</returns>
    public static T Effective<T>(ParseResult result, Option<T> option, T fromConfig) =>
        Effective(result, option, fromConfig, configured: true);

    /// <summary>
    /// Gets the value that wins between the command line and the configuration file. The configuration value wins only when it is set and the option was not given.
    /// </summary>
    /// <typeparam name="T">The value type of the option.</typeparam>
    /// <param name="result">The parse result.</param>
    /// <param name="option">The option to read.</param>
    /// <param name="fromConfig">The value from the configuration file.</param>
    /// <param name="configured">Whether the configuration file sets the value. When <see langword="false"/>, the option value or its default wins.</param>
    /// <returns>The given option value, otherwise <paramref name="fromConfig"/> when it is set, otherwise the option default.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static T Effective<T>(ParseResult result, Option<T> option, T fromConfig, bool configured)
    {
        ArgumentNullException.ThrowIfNull(result);
        return configured && result.GetResult(option) is null or { Implicit: true }
            ? fromConfig
            : result.GetValue(option)!;
    }

    /// <summary>
    /// Sets the frame count that the <c>ALLOC</c> line divides by to give the per-frame allocation.
    /// </summary>
    /// <param name="frames">The number of frames that the run rendered.</param>
    public void ReportFrames(long frames) => _frames = frames;

    /// <summary>
    /// Parses the arguments and runs the body. When the first argument is the subreaper mode, it runs the process reaper instead. With <c>--alloc-report</c>, it writes the <c>ALLOC</c> line after the body returns.
    /// </summary>
    /// <param name="args">The program arguments.</param>
    /// <param name="body">The program body. It runs after the <see cref="Prepare"/> steps and the <c>OPTIONS</c> line.</param>
    /// <returns>The exit status of the body, or the parser's status when parsing fails or <c>--help</c> is given.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> or <paramref name="body"/> is <see langword="null"/>.</exception>
    public int Run(string[] args, Func<ParseResult, int> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(args);
        if (args is [ProcessSubreaper.Mode, ..] && OperatingSystem.IsLinux())
        {
            return ProcessSubreaper.Run(args[1..]);
        }

        var baseline = AllocationReport.Capture();
        var wanted = false;

        Command.SetAction(result =>
        {
            foreach (var step in _prepare)
            {
                step(result);
            }

            if (ReportsOptions)
            {
                WriteOptions(result);
            }

            wanted = result.GetValue(_allocReport);
            return body(result);
        });

        var status = Command.Parse(args).Invoke();
        if (wanted)
        {
            BasinReport.Line(baseline.Since(_frames));
        }

        return status;
    }

    /// <summary>
    /// Sends <see cref="BasinLog"/> and libwayland diagnostics to standard error at the level that <c>--log-level</c> gives.
    /// </summary>
    /// <param name="result">The parse result.</param>
    /// <param name="trace">Whether to use the <c>debug</c> level when <c>--log-level</c> was not given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public void ConfigureLogging(ParseResult result, bool trace = false)
    {
        ArgumentNullException.ThrowIfNull(result);
        var name = result.GetValue(_logLevel)!;
        if (trace && result.GetResult(_logLevel) is null or { Implicit: true })
        {
            name = "debug";
        }

        BasinLogging.RouteToStandardError(BasinLogging.ParseLevel(name));
    }

    /// <summary>
    /// Writes the <c>OPTIONS</c> line with every reported option value. The call is removed from a Release build.
    /// </summary>
    /// <param name="result">The parse result.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    [Conditional("DEBUG")]
    public void WriteOptions(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        BasinReport.Line($"OPTIONS {string.Join(' ', _report.Select(entry => entry(result)))}");
    }

    /// <summary>
    /// Writes the help text for the command.
    /// </summary>
    /// <param name="result">The parse result.</param>
    /// <returns>Always 1, so a program can return it as a usage-error status.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public int Usage(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _ = new HelpAction().Invoke(result);
        return 1;
    }

    private static string Format(object? value) => value switch
    {
        null => "none",
        bool flag => flag ? "on" : "off",
        string text => text.Length == 0 ? "none" : text,
        Enum name => name.ToString().ToLowerInvariant(),
        IEnumerable list => FormatList(list),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() is { Length: > 0 } text ? text : "none",
    };

    private static string FormatList(IEnumerable list)
    {
        var parts = list.Cast<object?>().Select(Format).ToArray();
        return parts.Length == 0 ? "none" : string.Join(',', parts);
    }
}
