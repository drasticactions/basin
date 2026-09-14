using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Portal;
using Basin.Seat;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class ClipboardPortalTests
{
    private const string Impl = "org.freedesktop.impl.portal.Clipboard";
    private const string RemoteImpl = "org.freedesktop.impl.portal.RemoteDesktop";

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    private static (int Read, int Write) Pipe()
    {
        unsafe
        {
            var fds = stackalloc int[2];
            Assert.Equal(0, pipe2(fds, 0x80000));
            return (fds[0], fds[1]);
        }
    }

    private static string ReadAll(SafeHandle handle)
    {
        using var stream = new FileStream(new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: false), FileAccess.Read, 1, false);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string ReadAll(int fd)
    {
        using var stream = new FileStream(new SafeFileHandle(fd, ownsHandle: true), FileAccess.Read, 1, false);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static (PortalBus Bus, BasinServices Services, SeatSelectionStore Store) Setup(CompositorTestHost host, string address, AutoAnswerPrompts prompts)
    {
        var store = new SeatSelectionStore(host.Seat);
        var bus = new PortalBus(host.Loop, address, PortalBusTests.BusName);
        var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(bus)
            .Use<IScreenCapture>(new TestScreenCapture(host))
            .Use<IScreencastPublisher>(new TestScreencastPublisher())
            .Use<ISelectionStore>(store)
            .Use<IInputSink>(new RecordingInputSink())
            .Use<IPortalPrompts>(prompts)
            .Install(PortalPack.Default.Without("org.freedesktop.impl.portal.Screenshot").Without("org.freedesktop.impl.portal.InputCapture").Without("org.freedesktop.impl.portal.GlobalShortcuts"))
            .Freeze();
        PortalBusTests.Await(host, bus.Started);
        return (bus, services, store);
    }

    private static ObjectPath StartedSession(PortalTestClient frontend, CompositorTestHost host, bool requestClipboard)
    {
        var handle = new ObjectPath(PortalBus.RootPath + "/session/1_1/clip");
        var (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(RemoteImpl, "CreateSession", handle, "org.example.App", []));
        Assert.Equal(0u, response);
        if (requestClipboard)
        {
            PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "RequestClipboard", "oa{sv}", (ref MessageWriter w) =>
            {
                w.WriteObjectPath(handle);
                w.WriteDictionary(new Dictionary<string, VariantValue>());
            }));
        }

        (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(RemoteImpl, "SelectDevices", handle, "org.example.App", []));
        Assert.Equal(0u, response);
        Dictionary<string, VariantValue> results;
        (response, results) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(RemoteImpl, "Start", handle, "org.example.App", [], parentWindow: ""));
        Assert.Equal(0u, response);
        Assert.Equal(requestClipboard, results["clipboard_enabled"].GetBool());
        return handle;
    }

    [Fact]
    public void The_portal_can_own_the_clipboard_and_a_client_reads_through_a_transfer()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        var prompts = new AutoAnswerPrompts();
        var (bus, services, store) = Setup(host, daemon.Address, prompts);
        using (bus)
        using (services)
        {
            using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));
            var session = StartedSession(frontend, host, requestClipboard: true);
            var prompt = Assert.IsType<DevicePrompt>(Assert.Single(prompts.Asked));
            Assert.True(prompt.ClipboardRequested);

            var transfers = new List<(string Mime, uint Serial)>();
            var owners = new List<bool>();
            Action<Notification> onTransfer = n => { };
            using var transferWatch = PortalBusTests.Await(host, frontend.Connection.WatchSignalAsync<(ObjectPath, string, uint)>(
                PortalBusTests.BusName, PortalBus.RootPath, Impl, "SelectionTransfer",
                static (Message m, object? _) =>
                {
                    var r = m.GetBodyReader();
                    return (r.ReadObjectPath(), r.ReadString(), r.ReadUInt32());
                },
                (Notification<(ObjectPath, string, uint)> n) => { if (n.HasValue) { transfers.Add((n.Value.Item2, n.Value.Item3)); } },
                ObserverFlags.None, emitOnCapturedContext: false).AsTask());
            using var ownerWatch = PortalBusTests.Await(host, frontend.Connection.WatchSignalAsync<(ObjectPath, Dictionary<string, VariantValue>)>(
                PortalBusTests.BusName, PortalBus.RootPath, Impl, "SelectionOwnerChanged",
                static (Message m, object? _) =>
                {
                    var r = m.GetBodyReader();
                    return (r.ReadObjectPath(), r.ReadDictionaryOfStringToVariantValue());
                },
                (Notification<(ObjectPath, Dictionary<string, VariantValue>)> n) => { if (n.HasValue) { owners.Add(n.Value.Item2["session_is_owner"].GetBool()); } },
                ObserverFlags.None, emitOnCapturedContext: false).AsTask());

            PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "SetSelection", "oa{sv}", (ref MessageWriter w) =>
            {
                w.WriteObjectPath(session);
                w.WriteDictionary(new Dictionary<string, VariantValue> { ["mime_types"] = VariantValue.Array(new[] { "text/plain" }) });
            }));
            var types = new string[4];
            Assert.Equal(1, store.GetOffer(SelectionKind.Clipboard, types));
            Assert.Equal("text/plain", types[0]);
            PortalBusTests.PumpUntil(host, () => owners.Count == 1);
            Assert.True(owners[0]);

            var (readEnd, writeEnd) = Pipe();
            Assert.True(store.Receive(SelectionKind.Clipboard, "text/plain", new ClientFd(writeEnd, null)));
            PortalBusTests.PumpUntil(host, () => transfers.Count == 1);
            Assert.Equal("text/plain", transfers[0].Mime);
            var serial = transfers[0].Serial;

            using var handle = PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "SelectionWrite", "ou", (ref MessageWriter w) =>
            {
                w.WriteObjectPath(session);
                w.WriteUInt32(serial);
            }, static (Message m, object? _) => m.GetBodyReader().ReadHandle<SafeFileHandle>()!));
            using (var stream = new FileStream(new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: false), FileAccess.Write, 1, false))
            {
                stream.Write("hello"u8);
            }

            PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "SelectionWriteDone", "oub", (ref MessageWriter w) =>
            {
                w.WriteObjectPath(session);
                w.WriteUInt32(serial);
                w.WriteBool(true);
            }));
            handle.Dispose();
            Assert.Equal("hello", ReadAll(readEnd));
        }
    }

    [Fact]
    public void The_portal_reads_what_a_client_owns_and_is_refused_without_the_grant()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        var (bus, services, store) = Setup(host, daemon.Address, new AutoAnswerPrompts());
        using (bus)
        using (services)
        {
            using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));
            var session = StartedSession(frontend, host, requestClipboard: true);

            var source = new DataSource(["text/plain"], (mime, fd) =>
            {
                using var stream = new FileStream(new SafeFileHandle(fd.Value, ownsHandle: true), FileAccess.Write, 1, false);
                stream.Write("world"u8);
            });
            Assert.True(store.SetSelection(SelectionKind.Clipboard, source, SelectionSerial.Unchecked));

            using var handle = PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "SelectionRead", "os", (ref MessageWriter w) =>
            {
                w.WriteObjectPath(session);
                w.WriteString("text/plain");
            }, static (Message m, object? _) => m.GetBodyReader().ReadHandle<SafeFileHandle>()!));
            Assert.Equal("world", ReadAll(handle));

            var unrequested = new ObjectPath(PortalBus.RootPath + "/session/1_1/plain");
            var (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(RemoteImpl, "CreateSession", unrequested, "org.example.App", []));
            Assert.Equal(0u, response);
            var error = Assert.Throws<DBusErrorReplyException>(() => PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "SetSelection", "oa{sv}", (ref MessageWriter w) =>
            {
                w.WriteObjectPath(unrequested);
                w.WriteDictionary(new Dictionary<string, VariantValue> { ["mime_types"] = VariantValue.Array(new[] { "text/plain" }) });
            })));
            Assert.Equal(PortalError.NotAllowed, error.ErrorName);
        }
    }
}
