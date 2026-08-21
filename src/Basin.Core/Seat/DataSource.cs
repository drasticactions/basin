using Wayland;
using Wayland.Server;

namespace Basin;

public sealed class DataSource
{
    private readonly Action<string, ClientFd>? _send;
    private readonly Action? _cancel;

    public DataSource(WlDataSourceResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        MimeTypes = [];
        Resource = resource;
        resource.Offer += (_, e) => MimeTypes.Add(e.MimeType);
        resource.SetActions += (_, e) => Actions = (WlDataDeviceManager.DndAction)e.DndActions;
        resource.Destroyed += (_, _) => MarkDestroyed();
    }

    public DataSource(List<string> mimeTypes, Action<string, ClientFd> send, Action? cancel = null, WlClient? client = null)
    {
        MimeTypes = mimeTypes;
        _send = send;
        _cancel = cancel;
        _client = client;
    }

    private readonly WlClient? _client;

    public WlDataSourceResource? Resource { get; }

    public WlClient? Client => Resource?.Client ?? _client;

    public List<string> MimeTypes { get; }

    public WlDataDeviceManager.DndAction Actions { get; private set; }

    public bool IsDestroyed { get; private set; }

    public event Action? Destroyed;

    public void MarkDestroyed()
    {
        if (!IsDestroyed)
        {
            IsDestroyed = true;
            Destroyed?.Invoke();
        }
    }

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

    public void Target(string? mimeType)
    {
        if (!IsDestroyed)
        {
            Resource?.SendTarget(mimeType);
        }
    }

    public void Action(WlDataDeviceManager.DndAction action)
    {
        if (!IsDestroyed && Resource is { Version: >= 3 } resource)
        {
            resource.SendAction(action);
        }
    }

    public void DropPerformed()
    {
        if (!IsDestroyed && Resource is { Version: >= 3 } resource)
        {
            resource.SendDndDropPerformed();
        }
    }

    public void Finished()
    {
        if (!IsDestroyed && Resource is { Version: >= 3 } resource)
        {
            resource.SendDndFinished();
        }
    }

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
