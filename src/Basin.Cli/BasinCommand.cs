using System.Collections;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Basin.Cli;

public sealed class BasinCommand
{
    private readonly List<Func<ParseResult, string>> _report = [];
    private readonly HelpOption _help = new("--help", "-h");
    private readonly Option<string> _logLevel;
    private readonly Option<bool> _allocReport;
    private long _frames;

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

    public RootCommand Command { get; }

    public Option<T> Add<T>(Option<T> option)
    {
        ArgumentNullException.ThrowIfNull(option);
        Command.Options.Add(option);
        var key = option.Name.TrimStart('-');
        _report.Add(result => $"{key}={Format(result.GetValue(option))}");
        return option;
    }

    public void ReportFrames(long frames) => _frames = frames;

    public int Run(string[] args, Func<ParseResult, int> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var baseline = AllocationReport.Capture();
        var wanted = false;

        Command.SetAction(result =>
        {
            WriteOptions(result);
            wanted = result.GetValue(_allocReport);
            return body(result);
        });

        var status = Command.Parse(args).Invoke();
        if (wanted)
        {
            Console.WriteLine(baseline.Since(_frames));
        }

        return status;
    }

    public ILoggerFactory CreateLoggerFactory(ParseResult result, bool trace = false)
    {
        ArgumentNullException.ThrowIfNull(result);
        var name = result.GetValue(_logLevel)!;
        if (trace && result.GetResult(_logLevel) is null or { Implicit: true })
        {
            name = "debug";
        }

        return BasinLogging.Create(BasinLogging.ParseLevel(name));
    }

    public void WriteOptions(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Console.WriteLine("OPTIONS " + string.Join(' ', _report.Select(entry => entry(result))));
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
