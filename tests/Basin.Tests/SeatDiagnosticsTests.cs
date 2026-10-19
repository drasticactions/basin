using Basin.Capabilities;
using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public sealed class SeatDiagnosticsTests
{
    [Fact]
    public void A_click_traces_enter_button_and_focus_naming_the_surface_and_motion_only_on_request()
    {
        using var host = new CompositorTestHost(200, 100);
        var window = MappedToplevel.Map(host, host.Client, 60, 50, 0xFF0000FF);
        var services = new BasinServices(host.Loop);
        var diagnostics = new SeatDiagnostics(host.Seat, services);
        var events = new List<InputTraceEvent>();
        var moves = new List<InputTraceEvent>();
        using (diagnostics.WatchInput(motion: false, events.Add))
        using (diagnostics.WatchInput(motion: true, moves.Add))
        {
            host.Seat.Pointer.NotifyEnter(window.ServerSurface, 10, 12);
            host.Seat.Pointer.NotifyMotion(1, 14, 12);
            host.Seat.Pointer.NotifyButton(2, Basin.InputCodes.BtnLeft, true);
            host.Seat.Pointer.NotifyButton(3, Basin.InputCodes.BtnLeft, false);
            host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        }

        Assert.Equal(["enter", "button", "button", "keyboard-focus"], events.Select(e => e.Kind));
        Assert.All(events, e => Assert.Same(window.ServerSurface, e.Surface));
        Assert.True(events[1].Pressed);
        Assert.Equal(Basin.InputCodes.BtnLeft, events[1].Code);
        Assert.Equal(["enter", "motion", "button", "button", "keyboard-focus"], moves.Select(e => e.Kind));
        Assert.Equal(14, moves[1].X);

        host.Seat.Pointer.NotifyButton(4, Basin.InputCodes.BtnLeft, true);
        Assert.Equal(4, events.Count);
        host.Seat.Pointer.NotifyButton(5, Basin.InputCodes.BtnLeft, false);
    }
}
