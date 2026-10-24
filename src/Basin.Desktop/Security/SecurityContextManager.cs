using System.Runtime.InteropServices;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_security_context_manager_v1</c> global. It accepts clients on listen sockets that a sandbox engine supplies, and tags each client with a <see cref="SecurityContext"/>.
/// </summary>
public sealed class SecurityContextManager : IDisposable, IWpSecurityContextManagerV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityContextManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that the accepted clients connect to.</param>
    /// <param name="loop">The loop that watches the listen sockets.</param>
    public SecurityContextManager(WlServerDisplay display, ICompositorEventLoop loop)
    {
        _display = display;
        _loop = loop;
        _global = display.CreateGlobal(WpSecurityContextManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a listener accepts a client, after the client has its context.
    /// </summary>
    public event Action<WlClient, SecurityContext>? ClientConnected;

    /// <summary>
    /// Closes every listener and removes the global. Clients that connected stay connected.
    /// </summary>
    public void Dispose()
    {
        foreach (var listener in _listeners.ToArray())
        {
            listener.Close();
        }

        _global.Dispose();
    }

    /// <summary>
    /// Finds the security context of a client.
    /// </summary>
    /// <param name="client">The client to look up.</param>
    /// <returns>The context, or <see langword="null"/> when the client did not connect through a security context listener.</returns>
    public static SecurityContext? ContextOf(WlClient client) =>
        Contexts.GetValueOrDefault(client.RawHandle);

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new WpSecurityContextManagerV1Resource(client, version, id, ContextOf(client) is not null ? NestedManager.Instance : this);

    void IWpSecurityContextManagerV1Handler.CreateListener(WpSecurityContextManagerV1Resource resource, in WpSecurityContextManagerV1Resource.CreateListenerEventArgs args)
    {
        var client = resource.Client;
        if (args.ListenFd < 0)
        {
            var refused = new WpSecurityContextV1Resource(client, resource.Version, args.Id, RefusedContext.Instance);
            client.CloseFd(args.CloseFd);
            refused.PostError(ErrorInvalidListenFd, "The listening socket is not valid.");
            return;
        }

        _ = new PendingContext(this, client, resource.Version, args.Id, args.ListenFd, args.CloseFd);
    }

    void IWpSecurityContextManagerV1Handler.Destroy(WpSecurityContextManagerV1Resource resource)
    {
    }

    private sealed class NestedManager : IWpSecurityContextManagerV1Handler
    {
        public static readonly NestedManager Instance = new();

        void IWpSecurityContextManagerV1Handler.CreateListener(WpSecurityContextManagerV1Resource resource, in WpSecurityContextManagerV1Resource.CreateListenerEventArgs args)
        {
            resource.Client.CloseFd(args.ListenFd);
            resource.Client.CloseFd(args.CloseFd);
            resource.PostError(ErrorNested, "A client in a security context cannot make a new security context.");
        }

        void IWpSecurityContextManagerV1Handler.Destroy(WpSecurityContextManagerV1Resource resource)
        {
        }
    }

    private sealed class RefusedContext : IWpSecurityContextV1Handler
    {
        public static readonly RefusedContext Instance = new();

        void IWpSecurityContextV1Handler.SetSandboxEngine(WpSecurityContextV1Resource resource, in WpSecurityContextV1Resource.SetSandboxEngineEventArgs args)
        {
        }

        void IWpSecurityContextV1Handler.SetAppId(WpSecurityContextV1Resource resource, in WpSecurityContextV1Resource.SetAppIdEventArgs args)
        {
        }

        void IWpSecurityContextV1Handler.SetInstanceId(WpSecurityContextV1Resource resource, in WpSecurityContextV1Resource.SetInstanceIdEventArgs args)
        {
        }

        void IWpSecurityContextV1Handler.Commit(WpSecurityContextV1Resource resource)
        {
        }

        void IWpSecurityContextV1Handler.Destroy(WpSecurityContextV1Resource resource)
        {
        }
    }

    private sealed class PendingContext : IWpSecurityContextV1Handler
    {
        private readonly SecurityContextManager _owner;
        private readonly WpSecurityContextV1Resource _resource;
        private readonly int _listenFd;
        private readonly int _closeFd;
        private string? _engine;
        private string? _appId;
        private string? _instanceId;
        private bool _committed;

        public PendingContext(SecurityContextManager owner, WlClient client, uint version, uint id, int listenFd, int closeFd)
        {
            _owner = owner;
            var resource = new WpSecurityContextV1Resource(client, version, id, this);
            _resource = resource;
            _listenFd = listenFd;
            _closeFd = closeFd;

            resource.Destroyed += (_, _) =>
            {
                if (!_committed)
                {
                    resource.Client.CloseFd(_listenFd);
                    resource.Client.CloseFd(_closeFd);
                }
            };
        }

        void IWpSecurityContextV1Handler.SetSandboxEngine(WpSecurityContextV1Resource resource, in WpSecurityContextV1Resource.SetSandboxEngineEventArgs args) =>
            SetMetadata(ref _engine, args.Name);

        void IWpSecurityContextV1Handler.SetAppId(WpSecurityContextV1Resource resource, in WpSecurityContextV1Resource.SetAppIdEventArgs args) =>
            SetMetadata(ref _appId, args.AppId);

        void IWpSecurityContextV1Handler.SetInstanceId(WpSecurityContextV1Resource resource, in WpSecurityContextV1Resource.SetInstanceIdEventArgs args) =>
            SetMetadata(ref _instanceId, args.InstanceId);

        void IWpSecurityContextV1Handler.Commit(WpSecurityContextV1Resource resource)
        {
            if (_committed)
            {
                resource.PostError(ErrorAlreadyUsed, "The security context is already committed.");
                return;
            }

            _committed = true;
            var context = new SecurityContext(_engine, _appId, _instanceId);
            var listener = new Listener(_owner, context, resource.Client, _listenFd, _closeFd);
            _owner._listeners.Add(listener);
        }

        void IWpSecurityContextV1Handler.Destroy(WpSecurityContextV1Resource resource)
        {
        }

        private void SetMetadata(ref string? slot, string value)
        {
            if (_committed || slot is not null)
            {
                _resource.PostError(ErrorAlreadySet, "This metadata is already set.");
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
            catch (WaylandException error)
            {
                DesktopLog.Log.Warn(error, $"security-context: cannot create a client for an accepted connection");
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
