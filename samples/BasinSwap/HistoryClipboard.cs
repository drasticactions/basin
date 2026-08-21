using System.Diagnostics;
using Basin;
using Basin.Backend.Headless;
using Basin.Capabilities;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Render.Pixman;
using Basin.Scene;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Samples.Swap;

internal sealed class HistoryClipboard : ISelectionStore
{
    private readonly List<DataSource> _clipboard = [];
    private DataSource? _primary;

    public event Action<SelectionKind>? SelectionChanged;

    public int History => _clipboard.Count;

    public DataSource? Current(SelectionKind kind) =>
        kind == SelectionKind.Primary ? _primary : _clipboard.LastOrDefault(s => !s.IsDestroyed);

    public int GetOffer(SelectionKind kind, Span<string> types)
    {
        if (Current(kind) is not { } source)
        {
            return 0;
        }

        if (source.MimeTypes.Count > types.Length)
        {
            return -1;
        }

        for (var i = 0; i < source.MimeTypes.Count; i++)
        {
            types[i] = source.MimeTypes[i];
        }

        return source.MimeTypes.Count;
    }

    public bool SetSelection(SelectionKind kind, DataSource? source, uint serial)
    {
        if (kind == SelectionKind.Primary)
        {
            _primary = source;
        }
        else if (source is not null)
        {
            _clipboard.Add(source);
        }

        SelectionChanged?.Invoke(kind);
        return true;
    }

    public bool Receive(SelectionKind kind, string mimeType, ClientFd fd)
    {
        if (Current(kind) is not { } source)
        {
            fd.Close();
            return false;
        }

        source.Send(mimeType, fd);
        return true;
    }
}
