using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Hosted;
using SDL3;
using static Basin.Sdl.SdlLog;

namespace Basin.Sdl;

/// <summary>
/// Bridges the guest <see cref="ISelectionStore"/> and the SDL clipboard in both directions. The primary selection goes both ways as text only.
/// A guest selection is read ahead on worker threads, because the SDL data callback is synchronous.
/// </summary>
public sealed unsafe class SdlClipboard : ISdlEventSink, IDisposable
{
    /// <summary>
    /// The largest number of bytes that a read of one guest MIME type keeps.
    /// </summary>
    public const int PrefetchLimit = 4 * 1024 * 1024;

    private static readonly string[] TextMimes = ["text/plain;charset=utf-8", "text/plain", "UTF8_STRING", "TEXT", "STRING"];

    private readonly BasinCompositorHost _host;
    private readonly SdlLoop _loop;
    private readonly ThreadAffinity _thread;
    private readonly ISelectionStore _store;
    private readonly SDL.ClipboardDataCallback _data;
    private readonly SDL.ClipboardCleanupCallback _cleanup;
    private Offer? _offer;
    private long _nextOffer;
    private DataSource? _ownClipboard;
    private DataSource? _ownPrimary;
    private string? _lastPrimary;
    private bool _disposed;

    private sealed class Offer(long id, DataSource source, string[] mimes)
    {
        public long Id { get; } = id;

        public DataSource Source { get; } = source;

        public string[] Mimes { get; } = mimes;

        public ConcurrentDictionary<string, Task<byte[]?>> Fetches { get; } = new(StringComparer.Ordinal);

        public nint Native;

        public void FreeNative()
        {
            if (Native != 0)
            {
                NativeMemory.Free((void*)Native);
                Native = 0;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlClipboard"/> class and adds it to the loop as an event sink.
    /// </summary>
    /// <param name="host">The host whose selection store the bridge follows.</param>
    /// <param name="loop">The SDL loop that delivers the clipboard events and runs the posted results.</param>
    /// <exception cref="InvalidOperationException">The host has no <see cref="ISelectionStore"/>.</exception>
    public SdlClipboard(BasinCompositorHost host, SdlLoop loop)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(loop);
        _host = host;
        _loop = loop;
        _thread = loop.Affinity;
        _store = host.Services.Require<ISelectionStore>();
        _data = OnData;
        _cleanup = OnCleanup;
        _store.SelectionChanged += OnSelectionChanged;
        loop.AddSink(this);
    }

    /// <summary>
    /// Gets or sets the longest time that a read of one guest MIME type can take. The default is 4 seconds.
    /// </summary>
    public TimeSpan PrefetchTimeout { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Gets or sets the longest time that the SDL data callback waits for a read. It then answers with no data. The default is 500 ms.
    /// </summary>
    public TimeSpan CallbackWait { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets a value indicating whether the host clipboard holds a guest selection that the bridge offered.
    /// </summary>
    public bool OwnsHostClipboard => _offer is not null;

    /// <summary>
    /// Pulls the host clipboard into the guest on a clipboard update that the bridge did not cause.
    /// Reads the host primary selection again on a focus gain and a middle button press.
    /// </summary>
    /// <param name="e">The SDL event.</param>
    /// <returns><see langword="true"/> for a clipboard update, else <see langword="false"/>.</returns>
    public bool Handle(in SDL.Event e)
    {
        switch ((SDL.EventType)e.Type)
        {
            case SDL.EventType.ClipboardUpdate:
                Log.Debug($"clipboard: host update with {e.Clipboard.NumMimeTypes} types, ours={_offer is not null}");
                if (_offer is null)
                {
                    PullClipboard();
                }

                return true;
            case SDL.EventType.WindowFocusGained:
                PullPrimary();
                return false;
            case SDL.EventType.MouseButtonDown when e.Button.Button == 2:
                PullPrimary();
                return false;
            default:
                return false;
        }
    }

    private void OnSelectionChanged(SelectionKind kind)
    {
        if (_disposed)
        {
            return;
        }

        var source = _store.Current(kind);
        if (kind == SelectionKind.Clipboard)
        {
            if (source is null || ReferenceEquals(source, _ownClipboard))
            {
                return;
            }

            OfferToHost(source);
            return;
        }

        if (source is null || ReferenceEquals(source, _ownPrimary) || TextMimeOf(source) is not { } mime)
        {
            return;
        }

        var fetch = Fetch(kind, source, mime, PrefetchLimit, PrefetchTimeout);
        _ = fetch?.ContinueWith(
            done => _loop.Post(() =>
            {
                if (!_disposed && done.Result is { } bytes)
                {
                    _lastPrimary = Encoding.UTF8.GetString(bytes);
                    _ = SDL.SetPrimarySelectionText(_lastPrimary);
                }
            }),
            TaskScheduler.Default);
    }

    private void OfferToHost(DataSource source)
    {
        var mimes = source.MimeTypes.ToArray();
        if (mimes.Length == 0)
        {
            return;
        }

        var offer = new Offer(++_nextOffer, source, mimes);
        foreach (var mime in mimes)
        {
            if (Fetch(SelectionKind.Clipboard, source, mime, PrefetchLimit, PrefetchTimeout) is { } fetch)
            {
                offer.Fetches[mime] = fetch;
            }
        }

        var previous = _offer;
        _offer = offer;
        if (!SDL.SetClipboardData(_data, _cleanup, (nint)offer.Id, mimes, (nuint)mimes.Length))
        {
            Log.Warn($"clipboard: host offer failed, SDL_SetClipboardData reports \"{SDL.GetError()}\"");
            _offer = previous;
            return;
        }

        Log.Debug($"clipboard: {mimes.Length} types offered to the host");
    }

    private Task<byte[]?>? Fetch(SelectionKind kind, DataSource source, string mime, int limit, TimeSpan timeout)
    {
        if (source.Client is { FdSlots: { } slots } remote)
        {
            var inbound = new PipeFromClient(limit);
            if (!_store.Receive(kind, mime, new ClientFd(slots.Mint(inbound), remote)))
            {
                return null;
            }

            _host.Display.FlushClients();
            return Task.Run(() => inbound.ReadToEnd(timeout));
        }

        if (!SdlPipes.Open(out var readFd, out var writeFd))
        {
            return null;
        }

        if (!_store.Receive(kind, mime, new ClientFd(writeFd, source.Resource?.Client)))
        {
            SdlPipes.Close(readFd);
            SdlPipes.Close(writeFd);
            return null;
        }

        _host.Display.FlushClients();
        return Task.Run(() => SdlPipes.ReadAll(readFd, limit, timeout));
    }

    private nint OnData(nint userdata, string mimeType, out nuint size)
    {
        size = 0;
        try
        {
            if (_offer is not { } offer || offer.Id != userdata)
            {
                return 0;
            }

            if (!offer.Fetches.TryGetValue(mimeType, out var fetch) || (fetch.IsCompleted && fetch.Result is null))
            {
                fetch = Fetch(SelectionKind.Clipboard, offer.Source, mimeType, int.MaxValue, CallbackWait);
                if (fetch is null)
                {
                    return 0;
                }

                offer.Fetches[mimeType] = fetch;
            }

            if (!fetch.Wait(CallbackWait) || fetch.Result is not { } bytes)
            {
                Log.Debug($"clipboard: '{mimeType}' was not ready inside {CallbackWait.TotalMilliseconds} ms");
                return 0;
            }

            offer.FreeNative();
            offer.Native = (nint)NativeMemory.Alloc((nuint)Math.Max(1, bytes.Length));
            bytes.AsSpan().CopyTo(new Span<byte>((void*)offer.Native, bytes.Length));
            size = (nuint)bytes.Length;
            return offer.Native;
        }
        catch (Exception error)
        {
            Log.Error(error, $"clipboard: data callback failed");
            return 0;
        }
    }

    private void OnCleanup(nint userdata)
    {
        try
        {
            if (_offer is { } offer && offer.Id == userdata)
            {
                offer.FreeNative();
                _offer = null;
            }
        }
        catch (Exception error)
        {
            Log.Error(error, $"clipboard: cleanup callback failed");
        }
    }

    private void PullClipboard()
    {
        _thread.Assert();
        var mimes = SDL.GetClipboardMimeTypes(out _);
        if (mimes is not { Length: > 0 })
        {
            if (_ownClipboard is { } stale && ReferenceEquals(_store.Current(SelectionKind.Clipboard), stale))
            {
                _ownClipboard = null;
                _ = _store.SetSelection(SelectionKind.Clipboard, null, SelectionSerial.Unchecked);
            }

            return;
        }

        var source = new DataSource([.. mimes], SendFromHost);
        _ownClipboard = source;
        if (!_store.SetSelection(SelectionKind.Clipboard, source, SelectionSerial.Unchecked))
        {
            _ownClipboard = null;
        }
    }

    private void SendFromHost(string mime, ClientFd fd)
    {
        var pointer = SDL.GetClipboardData(mime, out var size);
        var bytes = pointer == 0 ? [] : new ReadOnlySpan<byte>((void*)pointer, (int)size).ToArray();
        if (pointer != 0)
        {
            SDL.Free(pointer);
        }

        Log.Debug($"clipboard: '{mime}' from the host is {bytes.Length} bytes");
        Deliver(fd, bytes);
    }

    private void PullPrimary()
    {
        _thread.Assert();
        var text = SDL.GetPrimarySelectionText();
        if (string.IsNullOrEmpty(text) || text == _lastPrimary)
        {
            return;
        }

        _lastPrimary = text;
        var bytes = Encoding.UTF8.GetBytes(text);
        var source = new DataSource([.. TextMimes], (_, fd) => Deliver(fd, bytes));
        _ownPrimary = source;
        if (!_store.SetSelection(SelectionKind.Primary, source, SelectionSerial.Unchecked))
        {
            _ownPrimary = null;
        }
    }

    private static void Deliver(ClientFd fd, byte[] bytes)
    {
        if (fd.Owner is { FdSlots: not null })
        {
            ChannelSelection.Write(fd, bytes);
            return;
        }

        _ = Task.Run(() =>
        {
            SdlPipes.WriteAll(fd.Value, bytes);
            fd.Close();
        });
    }

    private static string? TextMimeOf(DataSource source)
    {
        foreach (var offered in source.MimeTypes)
        {
            if (offered.StartsWith("text/plain", StringComparison.Ordinal) || offered == "UTF8_STRING")
            {
                return offered;
            }
        }

        return null;
    }

    /// <summary>
    /// Stops following the store, leaves the loop, and clears the host clipboard when the bridge owns it. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.SelectionChanged -= OnSelectionChanged;
        _loop.RemoveSink(this);
        if (_offer is not null)
        {
            _ = SDL.ClearClipboardData();
        }

        _offer?.FreeNative();
        _offer = null;
    }
}
