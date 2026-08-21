using System.Runtime.InteropServices;
using System.Text;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class DataDeviceTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe(int* fds);

    [DllImport("libc")]
    private static extern int close(int fd);

    private static (int Read, int Write) MakePipe()
    {
        unsafe
        {
            var fds = stackalloc int[2];
            Assert.Equal(0, pipe(fds));
            return (fds[0], fds[1]);
        }
    }

    private static void WriteAndClose(int fd, string text)
    {
        using var stream = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(fd, ownsHandle: true), FileAccess.Write);
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    private static string ReadAll(int fd)
    {
        using var stream = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(fd, ownsHandle: true), FileAccess.Read);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Copy_and_paste_between_two_clients()
    {
        using var host = new CompositorTestHost();
        var clientA = host.Client;
        var clientB = host.ConnectClient();
        var windowA = MappedToplevel.Map(host, clientA);
        var windowB = MappedToplevel.Map(host, clientB);

        var deviceA = clientA.DataDeviceManager!.GetDataDevice(clientA.Seat!);
        var source = clientA.DataDeviceManager.CreateDataSource();
        source.Offer("text/plain");
        var sent = false;
        source.Send += (_, e) =>
        {
            Assert.Equal("text/plain", e.MimeType);
            WriteAndClose(e.Fd, "basin clipboard payload");
            sent = true;
        };
        var canceled = false;
        source.Cancelled += (_, _) => canceled = true;
        deviceA.SetSelection(source, 0);
        host.PumpToServer();
        Assert.NotNull(host.Seat.DataDevice.Selection);

        var deviceB = clientB.DataDeviceManager!.GetDataDevice(clientB.Seat!);
        WlDataOffer? offer = null;
        var mimes = new List<string>();
        deviceB.DataOffer += (_, e) =>
        {
            offer = e.Id;
            offer.Offer += (_, o) => mimes.Add(o.MimeType);
        };
        var selectionSeen = false;
        deviceB.Selection += (_, e) => selectionSeen = e.Id is not null;
        host.PumpToClient();

        host.Seat.Keyboard.NotifyEnter(windowB.ServerSurface);
        host.PumpUntil(() => selectionSeen && offer is not null && mimes.Contains("text/plain"));

        var (readFd, writeFd) = MakePipe();
        offer!.Receive("text/plain", writeFd);
        clientB.Display.Flush();
        close(writeFd);
        host.PumpUntil(() => sent);

        Assert.Equal("basin clipboard payload", ReadAll(readFd));
        Assert.False(canceled);

        var source2 = clientA.DataDeviceManager.CreateDataSource();
        source2.Offer("text/plain");
        deviceA.SetSelection(source2, 0);
        host.PumpUntil(() => canceled);
    }

    [Fact]
    public void A_focus_enter_serial_can_set_the_selection()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);

        var keyboard = client.Seat!.GetKeyboard();
        uint enterSerial = 0;
        keyboard.Enter += (_, e) => enterSerial = e.Serial;
        host.PumpToServer();
        host.Seat.Keyboard.NotifyClearFocus();
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpUntil(() => enterSerial != 0);

        var device = client.DataDeviceManager!.GetDataDevice(client.Seat);
        var source = client.DataDeviceManager.CreateDataSource();
        source.Offer("text/plain");
        device.SetSelection(source, enterSerial);
        host.PumpToServer();
        Assert.NotNull(host.Seat.DataDevice.Selection);
        keyboard.Release();
    }

    [Fact]
    public void A_drag_icon_never_takes_drag_focus()
    {
        using var host = new CompositorTestHost();
        var clientA = host.Client;
        var clientB = host.ConnectClient();
        var windowA = MappedToplevel.Map(host, clientA);
        var windowB = MappedToplevel.Map(host, clientB);

        var deviceA = clientA.DataDeviceManager!.GetDataDevice(clientA.Seat!);
        var deviceB = clientB.DataDeviceManager!.GetDataDevice(clientB.Seat!);

        var pointerA = clientA.Seat!.GetPointer();
        uint pressSerial = 0;
        pointerA.Button += (_, e) =>
        {
            if (e.State == WlPointer.ButtonState.Pressed)
            {
                pressSerial = e.Serial;
            }
        };
        host.PumpToClient();
        host.Seat.Pointer.NotifyEnter(windowA.ServerSurface, 10, 10);
        host.Seat.Pointer.NotifyButton(1, 0x110, WlPointer.ButtonState.Pressed);
        host.PumpUntil(() => pressSerial != 0);

        var source = clientA.DataDeviceManager.CreateDataSource();
        source.Offer("text/uri-list");
        source.SetActions(WlDataDeviceManager.DndAction.Copy);
        var canceled = false;
        source.Cancelled += (_, _) => canceled = true;
        var icon = clientA.Compositor!.CreateSurface();
        deviceA.StartDrag(source, windowA.Surface, icon, pressSerial);
        host.PumpToServer();
        Assert.NotNull(host.Seat.DataDevice.DraggingIcon);

        var enterCount = 0;
        var leaveCount = 0;
        deviceB.DataOffer += (_, e) => e.Id.Offer += (_, _) => { };
        deviceB.Enter += (_, _) => enterCount++;
        deviceB.Leave += (_, _) => leaveCount++;
        host.PumpToClient();

        host.Seat.Pointer.NotifyEnter(windowB.ServerSurface, 7, 8);
        host.PumpUntil(() => enterCount == 1);

        host.Seat.Pointer.NotifyEnter(host.Seat.DataDevice.DraggingIcon!, 0, 0);
        host.PumpUntil(() => leaveCount == 1);
        Assert.Equal(1, enterCount);

        host.Seat.Pointer.NotifyButton(2, 0x110, WlPointer.ButtonState.Released);
        host.PumpUntil(() => canceled);
        Assert.Equal(1, enterCount);
    }

    [Fact]
    public void Unchanged_accept_and_actions_are_not_echoed()
    {
        using var host = new CompositorTestHost();
        var clientA = host.Client;
        var clientB = host.ConnectClient();
        var windowA = MappedToplevel.Map(host, clientA);
        var windowB = MappedToplevel.Map(host, clientB);

        var deviceA = clientA.DataDeviceManager!.GetDataDevice(clientA.Seat!);
        var deviceB = clientB.DataDeviceManager!.GetDataDevice(clientB.Seat!);

        var pointerA = clientA.Seat!.GetPointer();
        uint pressSerial = 0;
        pointerA.Button += (_, e) =>
        {
            if (e.State == WlPointer.ButtonState.Pressed)
            {
                pressSerial = e.Serial;
            }
        };
        host.PumpToClient();
        host.Seat.Pointer.NotifyEnter(windowA.ServerSurface, 10, 10);
        host.Seat.Pointer.NotifyButton(1, 0x110, WlPointer.ButtonState.Pressed);
        host.PumpUntil(() => pressSerial != 0);

        var source = clientA.DataDeviceManager.CreateDataSource();
        source.Offer("text/uri-list");
        source.SetActions(WlDataDeviceManager.DndAction.Copy | WlDataDeviceManager.DndAction.Move);
        var targetCount = 0;
        var sourceActionCount = 0;
        source.Target += (_, _) => targetCount++;
        source.Action += (_, _) => sourceActionCount++;
        deviceA.StartDrag(source, windowA.Surface, null, pressSerial);
        host.PumpToServer();

        WlDataOffer? offer = null;
        var offerActionCount = 0;
        deviceB.DataOffer += (_, e) =>
        {
            offer = e.Id;
            offer.Offer += (_, _) => { };
            offer.Action += (_, _) => offerActionCount++;
        };
        var entered = false;
        deviceB.Enter += (_, _) => entered = true;
        host.PumpToClient();

        host.Seat.Pointer.NotifyEnter(windowB.ServerSurface, 7, 8);
        host.PumpUntil(() => entered && offer is not null);

        offer!.Accept(0, "text/uri-list");
        offer.SetActions(WlDataDeviceManager.DndAction.Copy, WlDataDeviceManager.DndAction.Copy);
        clientB.Display.Flush();
        host.PumpUntil(() => targetCount == 1 && sourceActionCount == 1 && offerActionCount == 1);

        for (var i = 0; i < 3; i++)
        {
            offer.Accept(0, "text/uri-list");
            offer.SetActions(WlDataDeviceManager.DndAction.Copy, WlDataDeviceManager.DndAction.Copy);
            clientB.Display.Flush();
            host.PumpToServer();
            host.PumpToClient();
        }

        Assert.Equal(1, targetCount);
        Assert.Equal(1, sourceActionCount);
        Assert.Equal(1, offerActionCount);

        offer.SetActions(WlDataDeviceManager.DndAction.Move, WlDataDeviceManager.DndAction.Move);
        clientB.Display.Flush();
        host.PumpUntil(() => sourceActionCount == 2 && offerActionCount == 2);

        var dropPerformed = false;
        source.DndDropPerformed += (_, _) => dropPerformed = true;
        host.PumpToClient();
        host.Seat.Pointer.NotifyButton(2, 0x110, WlPointer.ButtonState.Released);
        host.PumpUntil(() => dropPerformed);
    }

    [Fact]
    public void Drag_and_drop_between_two_clients()
    {
        using var host = new CompositorTestHost();
        var clientA = host.Client;
        var clientB = host.ConnectClient();
        var windowA = MappedToplevel.Map(host, clientA);
        var windowB = MappedToplevel.Map(host, clientB);

        var deviceA = clientA.DataDeviceManager!.GetDataDevice(clientA.Seat!);
        var deviceB = clientB.DataDeviceManager!.GetDataDevice(clientB.Seat!);

        var pointerA = clientA.Seat!.GetPointer();
        uint pressSerial = 0;
        pointerA.Button += (_, e) =>
        {
            if (e.State == WlPointer.ButtonState.Pressed)
            {
                pressSerial = e.Serial;
            }
        };
        host.PumpToClient();
        host.Seat.Pointer.NotifyEnter(windowA.ServerSurface, 10, 10);
        host.Seat.Pointer.NotifyButton(1, 0x110, WlPointer.ButtonState.Pressed);
        host.PumpUntil(() => pressSerial != 0);

        var source = clientA.DataDeviceManager.CreateDataSource();
        source.Offer("text/uri-list");
        source.SetActions(WlDataDeviceManager.DndAction.Copy | WlDataDeviceManager.DndAction.Move);
        var sent = false;
        var dropPerformed = false;
        var finished = false;
        source.Send += (_, e) =>
        {
            WriteAndClose(e.Fd, "file:///tmp/basin-dnd");
            sent = true;
        };
        source.DndDropPerformed += (_, _) => dropPerformed = true;
        source.DndFinished += (_, _) => finished = true;
        deviceA.StartDrag(source, windowA.Surface, null, pressSerial);
        host.PumpToServer();

        WlDataOffer? offer = null;
        var entered = false;
        var dropped = false;
        deviceB.DataOffer += (_, e) =>
        {
            offer = e.Id;
            offer.Offer += (_, _) => { };
        };
        deviceB.Enter += (_, e) => entered = true;
        deviceB.Drop += (_, _) => dropped = true;
        host.PumpToClient();

        host.Seat.Pointer.NotifyEnter(windowB.ServerSurface, 7, 8);
        host.PumpUntil(() => entered && offer is not null);

        offer!.Accept(0, "text/uri-list");
        offer.SetActions(WlDataDeviceManager.DndAction.Copy, WlDataDeviceManager.DndAction.Copy);
        clientB.Display.Flush();
        host.PumpToClient();

        host.Seat.Pointer.NotifyButton(2, 0x110, WlPointer.ButtonState.Released);
        host.PumpUntil(() => dropped && dropPerformed);

        var (readFd, writeFd) = MakePipe();
        offer.Receive("text/uri-list", writeFd);
        clientB.Display.Flush();
        close(writeFd);
        host.PumpUntil(() => sent);
        Assert.Equal("file:///tmp/basin-dnd", ReadAll(readFd));

        offer.Finish();
        host.PumpUntil(() => finished);

        var motionSeen = false;
        var pointerB = clientB.Seat!.GetPointer();
        pointerB.Motion += (_, _) => motionSeen = true;
        host.PumpToClient();
        host.Seat.Pointer.NotifyEnter(windowB.ServerSurface, 1, 1);
        host.Seat.Pointer.NotifyMotion(3, 2, 2);
        host.PumpUntil(() => motionSeen);
    }

    [Fact]
    public void A_finish_after_the_source_died_does_not_kill_the_compositor()
    {
        using var host = new CompositorTestHost();
        var clientA = host.Client;
        var clientB = host.ConnectClient();
        var windowA = MappedToplevel.Map(host, clientA);
        var windowB = MappedToplevel.Map(host, clientB);

        var deviceA = clientA.DataDeviceManager!.GetDataDevice(clientA.Seat!);
        var deviceB = clientB.DataDeviceManager!.GetDataDevice(clientB.Seat!);

        var pointerA = clientA.Seat!.GetPointer();
        uint pressSerial = 0;
        pointerA.Button += (_, e) =>
        {
            if (e.State == WlPointer.ButtonState.Pressed)
            {
                pressSerial = e.Serial;
            }
        };
        host.PumpToClient();
        host.Seat.Pointer.NotifyEnter(windowA.ServerSurface, 10, 10);
        host.Seat.Pointer.NotifyButton(1, 0x110, WlPointer.ButtonState.Pressed);
        host.PumpUntil(() => pressSerial != 0);

        var source = clientA.DataDeviceManager.CreateDataSource();
        source.Offer("text/uri-list");
        source.SetActions(WlDataDeviceManager.DndAction.Move);
        var dropPerformed = false;
        source.DndDropPerformed += (_, _) => dropPerformed = true;
        deviceA.StartDrag(source, windowA.Surface, null, pressSerial);
        host.PumpToServer();

        WlDataOffer? offer = null;
        var entered = false;
        deviceB.DataOffer += (_, e) =>
        {
            offer = e.Id;
            offer.Offer += (_, _) => { };
        };
        deviceB.Enter += (_, _) => entered = true;
        host.PumpToClient();

        host.Seat.Pointer.NotifyEnter(windowB.ServerSurface, 7, 8);
        host.PumpUntil(() => entered && offer is not null);

        offer!.Accept(0, "text/uri-list");
        offer.SetActions(WlDataDeviceManager.DndAction.Move, WlDataDeviceManager.DndAction.Move);
        clientB.Display.Flush();
        host.PumpToClient();

        host.Seat.Pointer.NotifyButton(2, 0x110, WlPointer.ButtonState.Released);
        host.PumpUntil(() => dropPerformed);

        source.Destroy();
        clientA.Display.Flush();
        host.PumpToServer();

        offer.Finish();
        clientB.Display.Flush();
        host.PumpToServer();

        var motionSeen = false;
        var pointerB = clientB.Seat!.GetPointer();
        pointerB.Motion += (_, _) => motionSeen = true;
        host.PumpToClient();
        host.Seat.Pointer.NotifyEnter(windowB.ServerSurface, 1, 1);
        host.Seat.Pointer.NotifyMotion(3, 2, 2);
        host.PumpUntil(() => motionSeen);
    }
}
