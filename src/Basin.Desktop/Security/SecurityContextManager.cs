using System.Runtime.InteropServices;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class SecurityContextManager : IDisposable
{
    public const int Version = 1;

    private const uint ErrorInvalidListenFd = 1;
    private const uint ErrorNested = 2;
    private const uint ErrorAlreadyUsed = 1;
    private const uint ErrorAlreadySet = 2;

    [DllImport("libc", SetLastError = true)]
    private static extern int accept4(int fd, nint addr, nint addrlen, int flags);

    [DllImport("libc")]
    private static extern int close(int fd);

    private const int SockCloexec = 0x80000;

    private static readonly Dictionary<nint, SecurityContext> Contexts = [];

    private readonly WlServerDisplay _display;
    private readonly ICompositorEventLoop _loop;
    private readonly WlGlobal _global;
    private readonly List<Listener> _listeners = [];

    public SecurityContextManager(WlServerDisplay display, ICompositorEventLoop loop)
    {
        _display = display;
        _loop = loop;
        _global = display.CreateGlobal(WpSecurityContextManagerV1.Interface, Version, OnBind);
    }

    public event Action<WlClient, SecurityContext>? ClientConnected;

    public void Dispose()
    {
        foreach (var listener in _listeners.ToArray())
        {
            listener.Close();
        }

        _global.Dispose();
    }

    public static SecurityContext? ContextOf(WlClient client) =>
        Contexts.GetValueOrDefault(client.RawHandle);

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpSecurityContextManagerV1Resource(client, version, id);

        if (ContextOf(client) is not null)
        {
            manager.CreateListener += (_, e) =>
            {
                client.CloseFd(e.ListenFd);
                client.CloseFd(e.CloseFd);
                manager.PostError(ErrorNested, "nested security contexts are not allowed");
            };
            return;
        }

        manager.CreateListener += (_, e) =>
        {
            var resource = new WpSecurityContextV1Resource(client, manager.Version, e.Id);
            if (e.ListenFd < 0)
            {
                client.CloseFd(e.CloseFd);
                resource.PostError(ErrorInvalidListenFd, "invalid listening socket");
                return;
            }

            _ = new PendingContext(this, resource, e.ListenFd, e.CloseFd);
        };
    }

    private sealed class PendingContext
    {
        private readonly SecurityContextManager _owner;
        private readonly WpSecurityContextV1Resource _resource;
        private readonly int _listenFd;
        private readonly int _closeFd;
        private string? _engine;
        private string? _appId;
        private string? _instanceId;
        private bool _committed;

        public PendingContext(SecurityContextManager owner, WpSecurityContextV1Resource resource, int listenFd, int closeFd)
        {
            _owner = owner;
            _resource = resource;
            _listenFd = listenFd;
            _closeFd = closeFd;

            resource.SetSandboxEngine += (_, e) => SetMetadata(ref _engine, e.Name);
            resource.SetAppId += (_, e) => SetMetadata(ref _appId, e.AppId);
            resource.SetInstanceId += (_, e) => SetMetadata(ref _instanceId, e.InstanceId);
            resource.Commit += (_, _) =>
            {
                if (_committed)
                {
                    resource.PostError(ErrorAlreadyUsed, "security context already committed");
                    return;
                }

                _committed = true;
                var context = new SecurityContext(_engine, _appId, _instanceId);
                var listener = new Listener(_owner, context, resource.Client, _listenFd, _closeFd);
                _owner._listeners.Add(listener);
            };
            resource.Destroyed += (_, _) =>
            {
                if (!_committed)
                {
                    resource.Client.CloseFd(_listenFd);
                    resource.Client.CloseFd(_closeFd);
                }
            };
        }

        private void SetMetadata(ref string? slot, string value)
        {
            if (_committed || slot is not null)
            {
                _resource.PostError(ErrorAlreadySet, "metadata already set");
                return;
            }

            slot = value;
        }
    }

    private sealed class Listener
    {
        private readonly SecurityContextManager _owner;
        private readonly SecurityContext _context;
        private readonly WlClient _fdOwner;
        private readonly int _listenFd;
        private readonly int _closeFd;
        private IEventSource? _listenSource;
        private IEventSource? _closeSource;

        public Listener(SecurityContextManager owner, SecurityContext context, WlClient fdOwner, int listenFd, int closeFd)
        {
            _owner = owner;
            _context = context;
            _fdOwner = fdOwner;
            _listenFd = listenFd;
            _closeFd = closeFd;
            _listenSource = owner._loop.AddFd(listenFd, FdReadiness.Readable, (_, _) => Accept());
            _closeSource = owner._loop.AddFd(closeFd, FdReadiness.Readable, (_, _) => Close());
        }

        public void Close()
        {
            _listenSource?.Remove();
            _closeSource?.Remove();
            _listenSource = null;
            _closeSource = null;
            _fdOwner.CloseFd(_listenFd);
            _fdOwner.CloseFd(_closeFd);
            _owner._listeners.Remove(this);
        }

        private void Accept()
        {
            var fd = accept4(_listenFd, 0, 0, SockCloexec);
            if (fd < 0)
            {
                return;
            }

            WlClient client;
            try
            {
                client = _owner._display.CreateClient(fd);
            }
            catch (WaylandException)
            {
                close(fd);
                return;
            }

            var raw = client.RawHandle;
            Contexts[raw] = _context;
            client.Destroyed += () => Contexts.Remove(raw);
            _owner.ClientConnected?.Invoke(client, _context);
        }
    }
}
