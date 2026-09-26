using Basin.Capabilities;
using Basin.Ipc;
using Microsoft.Win32.SafeHandles;

namespace Basin.Tests;

internal sealed class TestSelectionStore : ISelectionStore, IDisposable
{
    private readonly Dictionary<SelectionKind, List<(string Mime, byte[]? Data)>> _offers = [];
    private readonly List<int> _held = [];
    private readonly List<Task> _writers = [];

    public event Action<SelectionKind>? SelectionChanged;

    public int Handlers => SelectionChanged?.GetInvocationList().Length ?? 0;

    public List<string> Received { get; } = [];

    public IReadOnlyList<int> Held => _held;

    public void Offer(SelectionKind kind, params (string Mime, byte[]? Data)[] offers)
    {
        _offers[kind] = [.. offers];
        SelectionChanged?.Invoke(kind);
    }

    public void Clear(SelectionKind kind)
    {
        _ = _offers.Remove(kind);
        SelectionChanged?.Invoke(kind);
    }

    public int GetOffer(SelectionKind kind, Span<string> types)
    {
        if (!_offers.TryGetValue(kind, out var offers))
        {
            return 0;
        }

        if (offers.Count > types.Length)
        {
            return -1;
        }

        for (var i = 0; i < offers.Count; i++)
        {
            types[i] = offers[i].Mime;
        }

        return offers.Count;
    }

    public DataSource? Current(SelectionKind kind) => null;

    public bool SetSelection(SelectionKind kind, DataSource? source, uint serial) => false;

    public bool Receive(SelectionKind kind, string mimeType, ClientFd fd)
    {
        Received.Add(mimeType);
        var data = _offers.TryGetValue(kind, out var offers) ? offers.Find(offer => offer.Mime == mimeType).Data : null;
        if (data is null)
        {
            _held.Add(fd.Value);
            return true;
        }

        _writers.Add(Task.Run(() =>
        {
            using var stream = new FileStream(new SafeFileHandle((nint)fd.Value, ownsHandle: true), FileAccess.Write, 1);
            try
            {
                stream.Write(data);
            }
            catch (IOException)
            {
            }
        }));
        return true;
    }

    public void Dispose()
    {
        foreach (var fd in _held)
        {
            _ = UnixSocket.Close(fd);
        }

        _held.Clear();
        Task.WaitAll([.. _writers], TimeSpan.FromSeconds(5));
    }
}
