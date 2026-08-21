using System.Text;
using Basin;
using Microsoft.Win32.SafeHandles;
using Wayland;

namespace Basin.Cli;

public sealed class StdinCommands
{
    private readonly Action<string> _handler;
    private readonly StringBuilder _pending = new();
    private readonly byte[] _chunk = new byte[512];
    private readonly FileStream _stdin;
    private IEventSource? _source;

    public StdinCommands(ICompositorEventLoop loop, Action<string> handler)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(handler);

        _handler = handler;
        _stdin = new FileStream(new SafeFileHandle(0, ownsHandle: false), FileAccess.Read);
        try
        {
            _source = loop.AddFd(0, FdReadiness.Readable, (_, _) => Drain());
        }
        catch (WaylandException)
        {
        }
    }

    public event Action<string, Exception>? CommandFailed;

    public bool IsOpen => _source is { IsRemoved: false };

    public void Stop()
    {
        if (_source is { IsRemoved: false } source)
        {
            source.Remove();
        }

        _source = null;
    }

    private void Drain()
    {
        var read = _stdin.Read(_chunk);
        if (read <= 0)
        {
            Stop();
            return;
        }

        _pending.Append(Encoding.UTF8.GetString(_chunk, 0, read));
        while (true)
        {
            var text = _pending.ToString();
            var newline = text.IndexOf('\n', StringComparison.Ordinal);
            if (newline < 0)
            {
                return;
            }

            _pending.Remove(0, newline + 1);
            var command = text[..newline];
            try
            {
                _handler(command);
            }
            catch (Exception error)
            {
                CommandFailed?.Invoke(command, error);
            }
        }
    }
}
