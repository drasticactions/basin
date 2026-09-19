using Basin.Capabilities;
using Basin.Eis;
using Libei;
using Xunit;

namespace Basin.Tests;

public sealed class EisReceiverTests
{
    [Fact(Skip = EisAvailability.Missing, SkipType = typeof(EisAvailability), SkipUnless = nameof(EisAvailability.Loaded))]
    public void A_sender_client_gets_devices_and_its_input_lands_on_the_sink()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingInputSink();
        var granted = InputDeviceCapability.Pointer | InputDeviceCapability.Keyboard;
        using var receiver = new EisReceiver(host.Loop, sink, host.Seat.Keyboard, granted);
        receiver.AddRegion("HEADLESS-1", 0, 0, 160, 120, 1);
        var fd = receiver.AddClientFd();

        using var ei = EiContext.CreateSender("basin-test");
        ei.ConnectToFd(fd);
        var devices = new List<EiDevice>();
        var resumed = 0;

        void PumpEi()
        {
            for (var round = 0; round < 10; round++)
            {
                host.Loop.Dispatch(0);
                ei.Dispatch();
                while (ei.TryGetEvent(out var @event))
                {
                    using (@event)
                    {
                        switch (@event.Type)
                        {
                            case EiEventType.SeatAdded:
                                using (var seat = @event.GetSeat())
                                {
                                    seat!.BindCapabilities(
                                        EiDeviceCapability.Pointer | EiDeviceCapability.PointerAbsolute |
                                        EiDeviceCapability.Button | EiDeviceCapability.Scroll | EiDeviceCapability.Keyboard);
                                }

                                break;

                            case EiEventType.DeviceAdded:
                                devices.Add(@event.GetDevice()!);
                                break;

                            case EiEventType.DeviceResumed:
                                resumed++;
                                break;
                        }
                    }
                }
            }
        }

        PumpEi();
        Assert.True(receiver.HasClient);
        Assert.Equal(3, receiver.DeviceCount);
        Assert.Equal(3, devices.Count);
        Assert.Equal(3, resumed);

        var relative = devices.Single(d => d.HasCapability(EiDeviceCapability.Pointer));
        var absolute = devices.Single(d => d.HasCapability(EiDeviceCapability.PointerAbsolute));
        var keyboard = devices.Single(d => d.HasCapability(EiDeviceCapability.Keyboard));
        if (host.Seat.Keyboard.KeymapBuffer is not null)
        {
            Assert.NotNull(keyboard.GetKeymap());
        }

        Assert.Single(absolute.GetRegions());
        Assert.Equal("HEADLESS-1", absolute.GetRegions()[0].MappingId);

        foreach (var device in devices)
        {
            device.StartEmulating(1);
        }

        relative.PointerMotion(4, -2);
        relative.Frame(0);
        absolute.PointerMotionAbsolute(30, 40);
        absolute.Frame(0);
        relative.Button(InputCodes.BtnLeft, true);
        relative.Frame(0);
        relative.ScrollDiscrete(0, 120);
        relative.Frame(0);
        keyboard.KeyboardKey(30, true);
        keyboard.Frame(0);
        keyboard.KeyboardKey(30, false);
        keyboard.Frame(0);
        PumpEi();

        Assert.True(receiver.IsEmulating);
        Assert.Equal(1, sink.CreatedKeyboards);
        Assert.Equal([(4.0, -2.0)], sink.Motions.Select(m => (m.Dx, m.Dy)));
        Assert.Equal([(30.0, 40.0, 160.0, 120.0)], sink.AbsoluteMotions.Select(m => (m.X, m.Y, m.Width, m.Height)));
        Assert.Equal([(InputCodes.BtnLeft, true)], sink.Buttons.Select(b => (b.Button, b.Pressed)));
        Assert.Equal([0u], sink.AxisSources);
        Assert.Equal([(0u, 15.0)], sink.Axes.Select(a => (a.Axis, a.Value)));
        Assert.Equal([(30u, true), (30u, false)], sink.Keys.Select(k => (k.Key, k.Pressed)));
        Assert.True(sink.Frames >= 5);

        foreach (var device in devices)
        {
            device.StopEmulating();
        }

        PumpEi();
        Assert.Equal([(InputCodes.BtnLeft, true), (InputCodes.BtnLeft, false)], sink.Buttons.Select(b => (b.Button, b.Pressed)));
        Assert.False(receiver.IsEmulating);

        foreach (var device in devices)
        {
            device.Dispose();
        }

        ei.Disconnect();
        PumpEi();
        Assert.False(receiver.HasClient);
    }

    [Fact(Skip = EisAvailability.Missing, SkipType = typeof(EisAvailability), SkipUnless = nameof(EisAvailability.Loaded))]
    public void A_receiver_client_is_refused()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingInputSink();
        using var receiver = new EisReceiver(host.Loop, sink, keymap: null, InputDeviceCapability.Pointer);
        var fd = receiver.AddClientFd();

        using var ei = EiContext.CreateReceiver("basin-test");
        ei.ConnectToFd(fd);
        var disconnected = false;
        for (var round = 0; round < 10 && !disconnected; round++)
        {
            host.Loop.Dispatch(0);
            ei.Dispatch();
            while (ei.TryGetEvent(out var @event))
            {
                using (@event)
                {
                    disconnected |= @event.Type == EiEventType.Disconnect;
                }
            }
        }

        Assert.True(disconnected);
        Assert.False(receiver.HasClient);
    }

    [Fact(Skip = EisAvailability.Missing, SkipType = typeof(EisAvailability), SkipUnless = nameof(EisAvailability.Loaded))]
    public void Disposing_while_a_key_is_held_releases_it()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingInputSink();
        var receiver = new EisReceiver(host.Loop, sink, keymap: null, InputDeviceCapability.Keyboard);
        var fd = receiver.AddClientFd();

        using var ei = EiContext.CreateSender("basin-test");
        ei.ConnectToFd(fd);
        EiDevice? keyboard = null;
        for (var round = 0; round < 10; round++)
        {
            host.Loop.Dispatch(0);
            ei.Dispatch();
            while (ei.TryGetEvent(out var @event))
            {
                using (@event)
                {
                    if (@event.Type == EiEventType.SeatAdded)
                    {
                        using var seat = @event.GetSeat();
                        seat!.BindCapabilities(EiDeviceCapability.Keyboard);
                    }
                    else if (@event.Type == EiEventType.DeviceAdded)
                    {
                        keyboard = @event.GetDevice();
                    }
                }
            }
        }

        Assert.NotNull(keyboard);
        keyboard.StartEmulating(1);
        keyboard.KeyboardKey(46, true);
        keyboard.Frame(0);
        for (var round = 0; round < 10; round++)
        {
            host.Loop.Dispatch(0);
            ei.Dispatch();
            while (ei.TryGetEvent(out var @event))
            {
                @event.Dispose();
            }
        }

        Assert.Equal([(46u, true)], sink.Keys.Select(k => (k.Key, k.Pressed)));
        receiver.Dispose();
        Assert.Equal([(46u, true), (46u, false)], sink.Keys.Select(k => (k.Key, k.Pressed)));
        keyboard.Dispose();
        ei.Disconnect();
    }
}
