using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public class TouchPointerEmulatorTests
{
    [Fact]
    public void A_client_that_bound_touch_is_left_to_the_touch_path()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);
        host.PumpToServer();
        _ = client.Seat!.GetTouch();
        host.PumpToServer();
        var emulator = new TouchPointerEmulator(host.Seat.Touch);

        Assert.False(emulator.TryClaim(0, window.ServerSurface));

        Assert.False(emulator.Active);
        Assert.Equal(-1, emulator.Slot);
    }

    [Fact]
    public void A_client_that_bound_no_touch_drives_the_pointer()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        host.PumpToServer();
        var emulator = new TouchPointerEmulator(host.Seat.Touch);

        Assert.True(emulator.TryClaim(0, window.ServerSurface));

        Assert.True(emulator.Active);
        Assert.Equal(0, emulator.Slot);
        Assert.True(emulator.Owns(0));
    }

    [Fact]
    public void Empty_space_drives_the_pointer()
    {
        using var host = new CompositorTestHost();
        var emulator = new TouchPointerEmulator(host.Seat.Touch);

        Assert.True(emulator.TryClaim(3, null));

        Assert.True(emulator.Owns(3));
    }

    [Fact]
    public void Only_one_contact_drives_the_pointer_at_a_time()
    {
        using var host = new CompositorTestHost();
        var emulator = new TouchPointerEmulator(host.Seat.Touch);
        Assert.True(emulator.TryClaim(0, null));

        Assert.False(emulator.TryClaim(1, null));

        Assert.Equal(0, emulator.Slot);
        Assert.False(emulator.Owns(1));
    }

    [Fact]
    public void Releasing_the_driving_contact_owes_one_button_release()
    {
        using var host = new CompositorTestHost();
        var emulator = new TouchPointerEmulator(host.Seat.Touch);
        Assert.True(emulator.TryClaim(0, null));

        Assert.True(emulator.Release(0));

        Assert.False(emulator.Release(0));
        Assert.False(emulator.Active);
    }

    [Fact]
    public void Releasing_another_contact_owes_nothing_and_keeps_the_driver()
    {
        using var host = new CompositorTestHost();
        var emulator = new TouchPointerEmulator(host.Seat.Touch);
        Assert.True(emulator.TryClaim(0, null));

        Assert.False(emulator.Release(1));

        Assert.True(emulator.Owns(0));
    }

    [Fact]
    public void Cancel_owes_a_release_only_while_a_contact_drives()
    {
        using var host = new CompositorTestHost();
        var emulator = new TouchPointerEmulator(host.Seat.Touch);

        Assert.False(emulator.Cancel());

        Assert.True(emulator.TryClaim(0, null));
        Assert.True(emulator.Cancel());
        Assert.False(emulator.Cancel());
        Assert.False(emulator.Active);
    }

    [Fact]
    public void A_released_contact_frees_the_pointer_for_the_next_one()
    {
        using var host = new CompositorTestHost();
        var emulator = new TouchPointerEmulator(host.Seat.Touch);
        Assert.True(emulator.TryClaim(0, null));
        Assert.True(emulator.Release(0));

        Assert.True(emulator.TryClaim(1, null));

        Assert.True(emulator.Owns(1));
    }
}
