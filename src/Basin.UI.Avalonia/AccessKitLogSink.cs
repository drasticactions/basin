using System.Text;
using AccessKit.Avalonia;
using Avalonia.Logging;
using Basin.Diagnostics;

namespace Basin.UI.Avalonia;

internal sealed class AccessKitLogSink(ILogSink? inner) : ILogSink
{
    public static readonly BasinLogger Output = BasinLog.For("avalonia");

    private static bool _installed;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Logger.Sink = new AccessKitLogSink(Logger.Sink);
    }

    public bool IsEnabled(LogEventLevel level, string area) =>
        area == AccessKitAvalonia.LogArea || inner?.IsEnabled(level, area) == true;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (area != AccessKitAvalonia.LogArea)
        {
            inner?.Log(level, area, source, messageTemplate, propertyValues);
            return;
        }

        var text = Format(messageTemplate, propertyValues);
        switch (level)
        {
            case LogEventLevel.Error or LogEventLevel.Fatal:
                Output.Error($"accessibility: {text}");
                break;
            case LogEventLevel.Warning:
                Output.Warn($"accessibility: {text}");
                break;
            default:
                Output.Debug($"accessibility: {text}");
                break;
        }
    }

    private static string Format(string template, object?[] values)
    {
        var text = new StringBuilder(template.Length);
        var next = 0;
        var at = 0;
        while (at < template.Length)
        {
            var open = template.IndexOf('{', at);
            var close = open < 0 ? -1 : template.IndexOf('}', open);
            if (close < 0)
            {
                text.Append(template, at, template.Length - at);
                break;
            }

            text.Append(template, at, open - at);
            text.Append(next < values.Length ? values[next++] : template[open..(close + 1)]);
            at = close + 1;
        }

        return text.ToString();
    }
}
