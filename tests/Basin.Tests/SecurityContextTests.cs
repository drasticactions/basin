using System.Runtime.InteropServices;
using System.Text;
using Basin.Desktop;
using Wayland;
using Wayland.Server;
using Xunit;

namespace Basin.Tests;

public sealed class GlobalFilterTests
{
    [Fact]
    public void Filtered_globals_are_invisible_to_the_client()
    {
        using var host = new CompositorTestHost();
        using var screencopy = new ScreencopyManager(host.Display, host.Layout, host.Buffers, capture: null);

        host.Display.SetGlobalFilter((client, global, interfaceName) =>
            interfaceName != "zwlr_screencopy_manager_v1");

        var seen = new List<string>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) => seen.Add(e.Interface);
        host.PumpToClient();

        Assert.DoesNotContain("zwlr_screencopy_manager_v1", seen);
        Assert.Contains("wl_compositor", seen);

        host.Display.SetGlobalFilter(null);
        var seenAfter = new List<string>();
        var registry2 = host.Client.Display.GetRegistry();
        registry2.Global += (_, e) => seenAfter.Add(e.Interface);
        host.PumpToClient();
        Assert.Contains("zwlr_screencopy_manager_v1", seenAfter);
    }
}

public sealed class SecurityContextTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int socket(int domain, int type, int protocol);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int bind(int fd, byte* addr, uint addrlen);

    [DllImport("libc", SetLastError = true)]
    private static extern int listen(int fd, int backlog);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int connect(int fd, byte* addr, uint addrlen);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe(int* fds);

    [DllImport("libc")]
    private static extern int close(int fd);

    private const int AfUnix = 1;
    private const int SockStream = 1;

    private static unsafe (int Fd, byte[] Addr) ListeningSocket(string path)
    {
        var fd = socket(AfUnix, SockStream, 0);
        Assert.True(fd >= 0);
        var addr = new byte[110];
        addr[0] = AfUnix;
        var bytes = Encoding.UTF8.GetBytes(path);
        bytes.CopyTo(addr, 2);
        fixed (byte* p = addr)
        {
            Assert.Equal(0, bind(fd, p, (uint)addr.Length));
        }

        Assert.Equal(0, listen(fd, 4));
        return (fd, addr);
    }

    [Fact]
    public void Accepted_clients_carry_their_declared_context()
    {
        using var host = new CompositorTestHost();
        using var manager = new SecurityContextManager(host.Display, host.Loop);
        var connected = new List<SecurityContext>();
        WlClient? sandboxed = null;
        manager.ClientConnected += (client, context) =>
        {
            sandboxed = client;
            connected.Add(context);
        };

        Basin.Desktop.Protocol.WpSecurityContextManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_security_context_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpSecurityContextManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var path = Path.Combine(Path.GetTempPath(), $"basin-secctx-{Environment.ProcessId}");
        File.Delete(path);
        var (listenFd, addr) = ListeningSocket(path);
        int closeRead, closeWrite;
        unsafe
        {
            var fds = stackalloc int[2];
            Assert.Equal(0, pipe(fds));
            closeRead = fds[0];
            closeWrite = fds[1];
        }

        var context = proxy!.CreateListener(listenFd, closeRead);
        close(listenFd);
        close(closeRead);
        context.SetSandboxEngine("org.flatpak");
        context.SetAppId("dev.example.Sandboxed");
        context.SetInstanceId("instance-7");
        context.Commit();
        host.PumpToServer();

        var appFd = socket(AfUnix, SockStream, 0);
        unsafe
        {
            fixed (byte* p = addr)
            {
                Assert.Equal(0, connect(appFd, p, (uint)addr.Length));
            }
        }

        host.PumpUntil(() => connected.Count == 1);
        Assert.Equal(new SecurityContext("org.flatpak", "dev.example.Sandboxed", "instance-7"), connected[0]);
        Assert.NotNull(sandboxed);
        Assert.Equal(connected[0], SecurityContextManager.ContextOf(sandboxed!));

        close(closeWrite);
        host.PumpToServer();
        var refusedFd = socket(AfUnix, SockStream, 0);
        unsafe
        {
            fixed (byte* p = addr)
            {
                _ = connect(refusedFd, p, (uint)addr.Length);
            }
        }

        host.PumpToServer();
        Assert.Single(connected);

        close(appFd);
        close(refusedFd);
        context.Dispose();
        File.Delete(path);
        host.PumpToServer();
    }
}
