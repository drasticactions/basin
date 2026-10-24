using System.Buffers;
using System.Text;
using System.Text.Json;
using Basin.Diagnostics;
using Microsoft.Win32.SafeHandles;
using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

/// <summary>
/// Runs the methods from text lines, such as the compositor's stdin, and prints each reply and event through the report channel.
/// </summary>
public sealed class IpcLineFront : IIpcReplySink, IIpcEventSubscriber, IDisposable
{
    private readonly IpcServer _server;
    private readonly IpcReply _reply;
    private readonly ArrayBufferWriter<byte> _params = new();
    private readonly Utf8JsonWriter _writer;
    private readonly List<byte> _pending = [];
    private readonly byte[] _chunk = new byte[512];
    private readonly FileStream? _input;
    private IEventSource? _source;
    private bool _disposed;

    internal IpcLineFront(IpcServer server, int fd)
    {
        _server = server;
        _reply = new IpcReply(this);
        _writer = new Utf8JsonWriter(_params);
        if (fd < 0)
        {
            return;
        }

        _input = new FileStream(new SafeFileHandle(fd, ownsHandle: false), FileAccess.Read);
        try
        {
            _source = server.Loop.AddFd(fd, FdReadiness.Readable, (_, _) => Drain());
        }
        catch (Exception exception)
        {
            Log.Debug($"fd {fd} cannot be watched, so it takes no commands: {exception.Message}");
        }
    }

    /// <summary>
    /// Gets a value indicating whether the front is not disposed.
    /// </summary>
    public bool IsOpen => !_disposed;

    /// <summary>
    /// Gets a value indicating whether the front still watches its descriptor. It stops at end of file, at a read error or at <see cref="Stop"/>.
    /// </summary>
    public bool IsReading => _source is { IsRemoved: false };

    /// <summary>
    /// Gets the client state of the front. Its owned resources live until the front is disposed.
    /// </summary>
    public IpcClientState State { get; } = new();

    /// <summary>
    /// Formats a reply as one line: <c>ERR code message</c>, or <c>OK method key=value ...</c> for the top-level keys of the result.
    /// </summary>
    /// <param name="reply">The finished reply.</param>
    /// <returns>The line.</returns>
    public static string Describe(IpcReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.IsError)
        {
            return $"ERR {reply.ErrorCode} {reply.ErrorMessage}";
        }

        var text = new StringBuilder("OK ").Append(reply.Method);
        var json = reply.ResultJson;
        var reader = new Utf8JsonReader(json);
        if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var key = reader.GetString();
                reader.Read();
                text.Append(' ').Append(key).Append('=');
                if (reader.TokenType == JsonTokenType.String)
                {
                    text.Append(reader.GetString());
                }
                else
                {
                    text.Append(Encoding.UTF8.GetString(IpcJson.RawValue(json, ref reader)));
                }
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Reads the <c>lines</c> array of a result, as <see cref="IpcLineReport"/> writes it.
    /// </summary>
    /// <param name="reply">The finished reply.</param>
    /// <returns>The lines, or an empty list for an error or a result with no <c>lines</c>.</returns>
    public static IReadOnlyList<string> Lines(IpcReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var lines = new List<string>();
        if (reply.IsError || !IpcJson.TryFindProperty(reply.ResultJson, "lines"u8, out var raw))
        {
            return lines;
        }

        var reader = new Utf8JsonReader(raw);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            return lines;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.String)
        {
            lines.Add(reader.GetString()!);
        }

        return lines;
    }

    /// <summary>
    /// Runs one line. <c>ipc METHOD {json}</c> calls any method. Another line matches the line forms by its first word. A line that matches nothing prints an <c>ERR</c> line.
    /// </summary>
    /// <param name="line">The line, without its line break.</param>
    /// <exception cref="ObjectDisposedException">The front is disposed.</exception>
    public void Execute(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return;
        }

        if (tokens[0] == "ipc")
        {
            ExecuteGeneric(line, tokens);
            return;
        }

        string? bindError = null;
        var matched = false;
        var usage = new List<string>();
        foreach (var form in _server.Methods.Lines)
        {
            if (!string.Equals(form.Pattern.Word, tokens[0], StringComparison.Ordinal))
            {
                continue;
            }

            matched = true;
            usage.Add(form.Pattern.Text);
            _params.ResetWrittenCount();
            _writer.Reset(_params);
            if (!form.Pattern.TryBind(tokens, _writer, out var error))
            {
                bindError ??= error;
                continue;
            }

            _writer.Flush();
            Run(form.Method, _params.WrittenSpan, form.Reply);
            return;
        }

        Report(matched
            ? $"ERR {IpcErrorCodes.InvalidParams} {bindError ?? "usage: " + string.Join(" | ", usage)}"
            : $"ERR {IpcErrorCodes.UnknownMethod} {tokens[0]}");
    }

    /// <summary>
    /// Prints a finished reply through its formatter, or through <see cref="Describe"/> when it has none. The front closes the reply's descriptors.
    /// </summary>
    /// <param name="reply">The finished reply.</param>
    public void Deliver(IpcReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        reply.ReleaseFds();
        string? text;
        try
        {
            text = reply.FrontState is IpcLineFormatter formatter ? formatter(reply) : Describe(reply);
        }
        catch (Exception exception)
        {
            Log.Error($"the line reply for {reply.Method} threw: {exception}");
            text = $"ERR {IpcErrorCodes.Internal} {exception.Message}";
        }

        if (text is not null)
        {
            Report(text);
        }
    }

    /// <summary>
    /// Stops reading the descriptor. <see cref="Execute"/> still runs lines, and events still print.
    /// </summary>
    public void Stop()
    {
        if (_source is { IsRemoved: false } source)
        {
            source.Remove();
        }

        _source = null;
    }

    /// <summary>
    /// Stops reading, removes the front's subscriptions and disposes its client state.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        _server.Events.UnsubscribeAll(this);
        _input?.Dispose();
        _writer.Dispose();
        State.Dispose();
    }

    /// <summary>
    /// Prints an event through the line formatter of its name, or as <c>EVENT name data</c>.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="message">The event envelope.</param>
    public void OnEvent(string name, ReadOnlySpan<byte> message)
    {
        if (_disposed || !IpcJson.TryFindProperty(message, "data"u8, out var data))
        {
            return;
        }

        string? text;
        try
        {
            text = _server.Events.LineFormatter(name) is { } format
                ? format(data)
                : $"EVENT {name} {Encoding.UTF8.GetString(data)}";
        }
        catch (Exception exception)
        {
            Log.Error($"the line for event {name} threw: {exception}");
            return;
        }

        if (text is not null)
        {
            Report(text);
        }
    }

    private static void Report(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            BasinReport.Line(line);
        }
    }

    private void ExecuteGeneric(string line, string[] tokens)
    {
        if (tokens.Length < 2)
        {
            Report($"ERR {IpcErrorCodes.InvalidParams} usage: ipc METHOD [{{json}}]");
            return;
        }

        var start = line.IndexOf(tokens[1], line.IndexOf("ipc", StringComparison.Ordinal) + 3, StringComparison.Ordinal)
            + tokens[1].Length;
        var json = Encoding.UTF8.GetBytes(line[start..].Trim());
        if (json.Length > 0 && !IsObject(json))
        {
            Report($"ERR {IpcErrorCodes.ParseError} the params are not a JSON object");
            return;
        }

        Run(tokens[1], json, null);
    }

    private static bool IsObject(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            reader.Skip();
            return !reader.Read();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void Run(string method, ReadOnlySpan<byte> parameters, IpcLineFormatter? formatter)
    {
        _ = _server.Invoke(method, default, parameters, _reply, formatter);
        if (!_reply.IsDeferred)
        {
            Deliver(_reply);
        }
    }

    private void Drain()
    {
        int read;
        try
        {
            read = _input!.Read(_chunk);
        }
        catch (IOException)
        {
            read = 0;
        }

        if (read <= 0)
        {
            Stop();
            return;
        }

        for (var i = 0; i < read; i++)
        {
            if (_chunk[i] != (byte)'\n')
            {
                _pending.Add(_chunk[i]);
                continue;
            }

            var line = Encoding.UTF8.GetString(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pending)).TrimEnd('\r');
            _pending.Clear();
            try
            {
                Execute(line);
            }
            catch (Exception exception)
            {
                Log.Error($"line '{line}' failed: {exception}");
                Report($"ERR {IpcErrorCodes.Internal} {exception.Message}");
            }

            if (_disposed)
            {
                return;
            }
        }
    }
}
