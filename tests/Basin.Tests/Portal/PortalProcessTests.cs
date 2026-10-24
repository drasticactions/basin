using System.Buffers.Binary;
using System.Diagnostics;
using Basin.Portal;
using Tmds.DBus.Protocol;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class PortalProcessTests
{
    private static string? Locate(string name)
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && directory is not null; i++)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    private static Task<bool> OwnedAsync(DBusConnection connection)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", "s");
            writer.WriteString(PortalBus.DefaultBusName);
            return connection.CallMethodAsync(writer.CreateMessage(), static (Message m, object? _) => m.GetBodyReader().ReadBool(), null);
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static Task<uint> DevicesAsync(DBusConnection connection)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(PortalBus.DefaultBusName, PortalBus.RootPath, "org.freedesktop.DBus.Properties", "Get", "ss");
            writer.WriteString("org.freedesktop.impl.portal.RemoteDesktop");
            writer.WriteString("AvailableDeviceTypes");
            return connection.CallMethodAsync(writer.CreateMessage(), static (Message m, object? _) => m.GetBodyReader().ReadVariantValue().GetUInt32(), null);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Theory]
    [InlineData("skia")]
    [InlineData("avalonia")]
    public async Task The_portal_runs_as_a_client_and_owns_its_name_against_TinyComp(string prompts)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("tinycomp");
        Assert.SkipWhen(compositor is null, "tinycomp has not been built beside the tests");
        var portal = Locate("xdg-desktop-portal-basin");
        Assert.SkipWhen(portal is null, "xdg-desktop-portal-basin has not been built beside the tests");

        using var bus = PrivateBus.Start();
        using var compositorProcess = StartCompositor(compositor!, bus.Address, out var socket);
        Assert.SkipWhen(socket is null, "the compositor printed no socket");

        using var portalProcess = StartPortal(portal!, bus.Address, socket!, prompts);
        try
        {
            using var connection = new DBusConnection(bus.Address);
            await connection.ConnectAsync();
            var owned = false;
            for (var i = 0; i < 100 && !owned; i++)
            {
                owned = await OwnedAsync(connection);
                if (!owned)
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }
            }

            Assert.True(owned, "the portal never owned its name");
            var devices = await DevicesAsync(connection);
            Assert.NotEqual(0u, devices & 3u);
        }
        finally
        {
            if (!portalProcess.HasExited)
            {
                portalProcess.Kill(entireProcessTree: true);
            }

            if (!compositorProcess.HasExited)
            {
                compositorProcess.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact]
    public async Task An_avalonia_prompt_is_a_window_named_by_its_title_that_a_reader_accepts()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("tinycomp");
        Assert.SkipWhen(compositor is null, "tinycomp has not been built beside the tests");
        var portal = Locate("xdg-desktop-portal-basin");
        Assert.SkipWhen(portal is null, "xdg-desktop-portal-basin has not been built beside the tests");

        using var a11y = await AccessibilityBus.StartAsync();
        using var compositorProcess = StartCompositor(compositor!, a11y.SessionAddress, out var socket);
        Assert.SkipWhen(socket is null, "the compositor printed no socket");
        using var portalProcess = StartPortal(portal!, a11y.SessionAddress, socket!, "avalonia", a11y: a11y.A11yAddress);
        try
        {
            using var connection = new DBusConnection(a11y.SessionAddress);
            await connection.ConnectAsync();
            for (var i = 0; i < 100 && !await OwnedAsync(connection); i++)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            using var frontend = await PortalTestClient.ConnectAsync(a11y.SessionAddress, PortalBus.DefaultBusName, asFrontend: true);
            var handle = frontend.NextRequest();
            var call = frontend.CallRawAsync(
                "org.freedesktop.impl.portal.Access",
                "AccessDialog",
                "osssssa{sv}",
                (ref MessageWriter writer) =>
                {
                    writer.WriteObjectPath(handle);
                    writer.WriteString("org.example.App");
                    writer.WriteString("");
                    writer.WriteString("Allow access?");
                    writer.WriteString("org.example.App wants access");
                    writer.WriteString("");
                    writer.WriteDictionary(new Dictionary<string, VariantValue>());
                },
                static (Message m, object? _) => PortalTestClient.ReadResponse(m));

            using var reader = await a11y.ConnectAsync();
            await reader.FindAsync("Allow access?", "frame");
            var allow = await reader.FindAsync("Allow", "push button");
            Assert.True(await reader.DoActionAsync(allow, "click"));
            var (response, _) = await call.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(PortalResponse.Success, response);
        }
        finally
        {
            if (!portalProcess.HasExited)
            {
                portalProcess.Kill(entireProcessTree: true);
            }

            if (!compositorProcess.HasExited)
            {
                compositorProcess.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact]
    public async Task The_out_of_process_pick_reads_the_clicked_pixel_of_a_client()
    {
        using var session = await Session.StartAsync();
        var (response, results) = await session.WhileDriving(
            session.Portal.CallImplAsync("org.freedesktop.impl.portal.Screenshot", "PickColor", "org.example.App", "", []),
            "move 400 300", "button 272 1", "button 272 0");
        Assert.Equal(0u, response);
        var color = results["color"];
        Assert.Equal(0x33 / 255.0, color.GetItem(0).GetDouble(), 0.005);
        Assert.Equal(0x66 / 255.0, color.GetItem(1).GetDouble(), 0.005);
        Assert.Equal(0x99 / 255.0, color.GetItem(2).GetDouble(), 0.005);
    }

    [Fact]
    public async Task The_out_of_process_area_screenshot_has_the_size_of_the_area()
    {
        using var session = await Session.StartAsync();
        var (response, results) = await session.WhileDriving(
            session.Portal.CallScreenshotAsync("org.example.App", "", new Dictionary<string, VariantValue> { ["interactive"] = VariantValue.Bool(true) }),
            "move 100 100", "button 272 1", "move 200 150", "move 300 200", "button 272 0");
        Assert.Equal(0u, response);
        var path = new Uri(results["uri"].GetString()).LocalPath;
        var header = new byte[24];
        using (var file = File.OpenRead(path))
        {
            file.ReadExactly(header);
        }

        Assert.Equal((200, 100), (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20))));
    }

    private sealed class Session : IDisposable
    {
        private readonly PrivateBus _bus;
        private readonly Process _compositor;
        private readonly Process _portalProcess;
        private readonly string _cache;
        private readonly ShmTestClient _client;

        private Session(PrivateBus bus, Process compositor, Process portal, string cache, ShmTestClient client, PortalTestClient portalClient)
        {
            _bus = bus;
            _compositor = compositor;
            _portalProcess = portal;
            _cache = cache;
            _client = client;
            Portal = portalClient;
        }

        public PortalTestClient Portal { get; }

        public static async Task<Session> StartAsync()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
            var compositor = Locate("tinycomp");
            Assert.SkipWhen(compositor is null, "tinycomp has not been built beside the tests");
            var portal = Locate("xdg-desktop-portal-basin");
            Assert.SkipWhen(portal is null, "xdg-desktop-portal-basin has not been built beside the tests");

            var bus = PrivateBus.Start();
            var compositorProcess = StartCompositor(compositor!, bus.Address, out var socket);
            Assert.SkipWhen(socket is null, "the compositor printed no socket");
            var client = Fullscreen(socket!, 0xFF336699);
            var cache = Directory.CreateTempSubdirectory("basin-portal-cache").FullName;
            var portalProcess = StartPortal(portal!, bus.Address, socket!, "skia", cache);
            var portalClient = await PortalTestClient.ConnectAsync(bus.Address, PortalBus.DefaultBusName, asFrontend: false);
            var owned = false;
            for (var i = 0; i < 100 && !owned; i++)
            {
                owned = await OwnedAsync(portalClient.Connection);
                if (!owned)
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }
            }

            Assert.True(owned, "the portal never owned its name");
            return new Session(bus, compositorProcess, portalProcess, cache, client, portalClient);
        }

        public async Task<T> WhileDriving<T>(Task<T> call, params string[] commands)
        {
            await Task.Delay(1500, TestContext.Current.CancellationToken);
            foreach (var command in commands)
            {
                _compositor.StandardInput.WriteLine(command);
                _compositor.StandardInput.Flush();
                await Task.Delay(200, TestContext.Current.CancellationToken);
            }

            return await call.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        public void Dispose()
        {
            Portal.Dispose();
            foreach (var process in new[] { _portalProcess, _compositor })
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                process.WaitForExit();
                process.Dispose();
            }

            _client.Dispose();
            _bus.Dispose();
            Directory.Delete(_cache, recursive: true);
        }

        private static ShmTestClient Fullscreen(string socket, uint color)
        {
            var client = new ShmTestClient(WlDisplay.Connect(socket));
            client.BindGlobals(() => client.Display.Roundtrip());
            var surface = client.Compositor.CreateSurface();
            var xdgSurface = client.WmBase!.GetXdgSurface(surface);
            var toplevel = xdgSurface.GetToplevel();
            toplevel.SetAppId("basin-test");
            toplevel.SetFullscreen(null);
            var (width, height, serial) = (0, 0, 0u);
            toplevel.Configure += (_, e) => (width, height) = (e.Width, e.Height);
            xdgSurface.Configure += (_, e) => serial = e.Serial;
            surface.Commit();
            for (var i = 0; i < 50 && (serial == 0 || width == 0); i++)
            {
                client.Display.Roundtrip();
            }

            Assert.True(width > 0 && height > 0, "the client was never configured fullscreen");
            xdgSurface.AckConfigure(serial);
            var buffer = client.CreateBuffer(width, height, (data, stride) =>
            {
                Fill.Solid(width, height, 0xFF000000)(data, stride);
                unsafe
                {
                    for (var y = height / 4; y < height * 3 / 4; y++)
                    {
                        new Span<uint>((uint*)(data + (y * stride)) + (width / 4), width / 2).Fill(color);
                    }
                }
            });
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, width, height);
            surface.Commit();
            client.Display.Roundtrip();
            return client;
        }
    }

    private static Process StartCompositor(string path, string busAddress, out string? socket)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("--backend");
        info.ArgumentList.Add("headless");
        info.ArgumentList.Add("--renderer");
        info.ArgumentList.Add("pixman");
        info.ArgumentList.Add("--config");
        info.ArgumentList.Add("false");
        info.Environment["DBUS_SESSION_BUS_ADDRESS"] = busAddress;
        info.Environment["XDG_CURRENT_DESKTOP"] = "basin";
        var process = Process.Start(info)!;
        socket = null;
        for (var i = 0; i < 100; i++)
        {
            var line = process.StandardOutput.ReadLine();
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("SOCKET ", StringComparison.Ordinal))
            {
                socket = line.Split(' ')[1];
                break;
            }
        }

        _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        return process;
    }

    private static Process StartPortal(string path, string busAddress, string socket, string prompts, string? cache = null, string? a11y = null)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("--socket");
        info.ArgumentList.Add(socket);
        info.ArgumentList.Add("--prompts");
        info.ArgumentList.Add(prompts);
        info.Environment["DBUS_SESSION_BUS_ADDRESS"] = busAddress;
        info.Environment["XDG_CURRENT_DESKTOP"] = "basin";
        info.Environment["AT_SPI_BUS_ADDRESS"] = a11y ?? "unix:path=/nonexistent/basin-portal-tests/at-spi";

        if (cache is not null)
        {
            info.Environment["XDG_CACHE_HOME"] = cache;
        }

        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }
}
