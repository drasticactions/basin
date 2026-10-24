using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// A selection or drag-and-drop source. It wraps a client's <c>wl_data_source</c>, or a compositor-side source that answers through delegates.
/// </summary>
public sealed class DataSource
{
    private readonly Action<string, ClientFd>? _send;
    private readonly Action? _cancel;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSource"/> class for a client resource. It records the client's offers and actions as the requests arrive.
    /// </summary>
    /// <param name="resource">The client's data source resource.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    public DataSource(WlDataSourceResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        MimeTypes = [];
        Resource = resource;
        resource.Offer += (_, e) => MimeTypes.Add(e.MimeType);
        resource.SetActions += (_, e) => Actions = (WlDataDeviceManager.DndAction)e.DndActions;
        resource.Destroyed += (_, _) => MarkDestroyed();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSource"/> class with no resource. Transfers and cancellation go to the delegates.
    /// </summary>
    /// <param name="mimeTypes">The offered MIME types. The source keeps this list and does not copy it.</param>
    /// <param name="send">Called for each transfer with the MIME type and the write end. The delegate owns the descriptor and must close it.</param>
    /// <param name="cancel">Called when the source is cancelled, or <see langword="null"/>.</param>
    /// <param name="client">The client that the source speaks for, or <see langword="null"/> for the compositor.</param>
    public DataSource(List<string> mimeTypes, Action<string, ClientFd> send, Action? cancel = null, WlClient? client = null)
    {
        MimeTypes = mimeTypes;
        _send = send;
        _cancel = cancel;
        _client = client;
    }

    private readonly WlClient? _client;

    /// <summary>Gets the client resource, or <see langword="null"/> for a source that delegates its transfers.</summary>
    public WlDataSourceResource? Resource { get; }

    /// <summary>Gets the owning client, or <see langword="null"/> for a compositor-owned source.</summary>
    public WlClient? Client => Resource?.Client ?? _client;

    /// <summary>Gets the offered MIME types. A client resource adds to it on each <c>offer</c> request.</summary>
    public List<string> MimeTypes { get; }

    /// <summary>Gets the drag-and-drop actions that the client last offered. It is none for a source that delegates.</summary>
    public WlDataDeviceManager.DndAction Actions { get; private set; }

    /// <summary>Gets a value indicating whether the source is destroyed. A destroyed source sends no events and closes each transfer fd.</summary>
    public bool IsDestroyed { get; private set; }

    /// <summary>Occurs once, when the resource is destroyed or <see cref="MarkDestroyed"/> runs first.</summary>
    public event Action? Destroyed;

    /// <summary>
    /// Marks the source destroyed and raises <see cref="Destroyed"/>. A second call does nothing.
    /// </summary>
    public void MarkDestroyed()
    {
        if (!IsDestroyed)
        {
            IsDestroyed = true;
            Destroyed?.Invoke();
        }
    }

    /// <summary>
    /// Asks the source to write one MIME type into a pipe. It takes ownership of <paramref name="fd"/> in all cases.
    /// </summary>
    /// <param name="mimeType">The MIME type to write.</param>
    /// <param name="fd">The write end of the pipe, owned by the receiving client's fd table.</param>
    /// <remarks>
    /// When the source and the receiver use different fd tables, the call relays through a token pipe if it can. Otherwise it closes the pipe, and the receiver reads no data.
    /// </remarks>
    public void Send(string mimeType, ClientFd fd)
    {
        if (IsDestroyed)
        {
            fd.Close();
            return;
        }

        if (Client is { } client && !ReferenceEquals(client.FdSlots, fd.Owner?.FdSlots))
        {
            if (client.FdSlots is { } sourceSlots && ResolvePipe(fd) is { CanWrite: true } sink)
            {
                fd.Close();
                Dispatch(mimeType, new ClientFd(sourceSlots.Mint(new PipeRelay(sink)), client));
                return;
            }

            ResolvePipe(fd)?.CloseWrite();
            fd.Close();
            return;
        }

        Dispatch(mimeType, fd);
    }

    private void Dispatch(string mimeType, ClientFd fd)
    {
        if (Resource is { } resource)
        {
            resource.SendSend(mimeType, fd.Value);
            fd.Close();
        }
        else
        {
            _send!(mimeType, fd);
        }
    }

    private static IPipeToClient? ResolvePipe(ClientFd fd)
    {
        if (fd.Owner?.FdSlots is not { } slots || fd.Value < 0)
        {
            return null;
        }

        try
        {
            return slots.Resolve<object>(fd.Value) as IPipeToClient;
        }
        catch (Exception error) when (
            error is ArgumentException or InvalidOperationException or KeyNotFoundException or ObjectDisposedException)
        {
            return null;
        }
    }

    private sealed class PipeRelay(IPipeToClient sink) : IPipeFromClient
    {
        public void Deliver(ReadOnlySpan<byte> bytes) => sink.Write(bytes);

        public void Complete() => sink.CloseWrite();
    }

    /// <summary>
    /// Tells the client which MIME type the drag target accepts. It does nothing for a destroyed or delegating source.
    /// </summary>
    /// <param name="mimeType">The accepted MIME type, or <see langword="null"/> when the target accepts none.</param>
    public void Target(string? mimeType)
    {
        if (!IsDestroyed)
        {
            Resource?.SendTarget(mimeType);
        }
    }

    /// <summary>
    /// Tells the client which drag-and-drop action the compositor selected. Only version 3 or later resources get the event.
    /// </summary>
    /// <param name="action">The selected action.</param>
    public void Action(WlDataDeviceManager.DndAction action)
    {
        if (!IsDestroyed && Resource is { Version: >= 3 } resource)
        {
            resource.SendAction(action);
        }
    }

    /// <summary>
    /// Tells the client that the user dropped. Only version 3 or later resources get the event.
    /// </summary>
    public void DropPerformed()
    {
        if (!IsDestroyed && Resource is { Version: >= 3 } resource)
        {
            resource.SendDndDropPerformed();
        }
    }

    /// <summary>
    /// Tells the client that the target finished the drag-and-drop transfer. Only version 3 or later resources get the event.
    /// </summary>
    public void Finished()
    {
        if (!IsDestroyed && Resource is { Version: >= 3 } resource)
        {
            resource.SendDndFinished();
        }
    }

    /// <summary>
    /// Tells the source that it is no longer the selection or that the drag failed. A delegating source calls its cancel delegate.
    /// </summary>
    public void Cancel()
    {
        if (IsDestroyed)
        {
            return;
        }

        if (Resource is { } resource)
        {
            resource.SendCancelled();
        }
        else
        {
            _cancel?.Invoke();
        }
    }
}
