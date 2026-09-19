using Basin.Hosted;
using Basin.Shell.Nested;
using Wayland;
using Xunit;
using static Basin.Tests.Nested.NestedShellHarness;

namespace Basin.Tests.Nested;

public sealed class NestedShellTouchTests
{
    private const uint BtnLeft = 0x110;
    private const uint BtnRight = 0x111;

    [Fact]
    public void A_touch_on_a_client_surface_reaches_it_as_wl_touch()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var touch = harness.Client.Seat!.GetTouch();
        var log = new List<string>();
        touch.Down += (_, e) => log.Add($"down {e.Id} {e.X.ToDouble():F0},{e.Y.ToDouble():F0}");
        touch.Motion += (_, e) => log.Add($"motion {e.Id} {e.X.ToDouble():F0},{e.Y.ToDouble():F0}");
        touch.Up += (_, e) => log.Add($"up {e.Id}");
        touch.Frame += (_, _) => log.Add("frame");
        touch.Cancel += (_, _) => log.Add("cancel");
        harness.Pump();
        var toplevel = harness.MapToplevel(width: 200, height: 150);
        var client = shell.Windows[0].ClientBox;

        Touch(shell, BasinViewInputKind.TouchDown, 3, client.X + 5, client.Y + 7);
        Touch(shell, BasinViewInputKind.TouchMotion, 3, client.X + 25, client.Y + 17);
        Touch(shell, BasinViewInputKind.TouchUp, 3, client.X + 25, client.Y + 17);
        harness.PumpUntil(() => log.Count >= 6, "the touch sequence never reached the client");
        Assert.Equal(["down 3 5,7", "frame", "motion 3 25,17", "frame", "up 3", "frame"], log);
        Assert.False(shell.TouchGestureActive);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void A_client_with_only_a_pointer_gets_the_emulated_press()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var pointer = harness.Client.Seat!.GetPointer();
        var log = new List<string>();
        pointer.Enter += (_, e) => log.Add($"enter {e.SurfaceX.ToDouble():F0},{e.SurfaceY.ToDouble():F0}");
        pointer.Motion += (_, e) => log.Add($"motion {e.SurfaceX.ToDouble():F0},{e.SurfaceY.ToDouble():F0}");
        pointer.Button += (_, e) => log.Add($"{(e.State == WlPointer.ButtonState.Pressed ? "press" : "release")} {e.Button:x}");
        harness.Pump();
        var toplevel = harness.MapToplevel(width: 200, height: 150);
        var client = shell.Windows[0].ClientBox;

        Touch(shell, BasinViewInputKind.TouchDown, 1, client.X + 5, client.Y + 7);
        Touch(shell, BasinViewInputKind.TouchMotion, 1, client.X + 25, client.Y + 17);
        Touch(shell, BasinViewInputKind.TouchUp, 1, client.X + 25, client.Y + 17);
        harness.PumpUntil(() => log.Contains("release 110"), "the emulated release never reached the client");
        Assert.Contains("enter 5,7", log);
        Assert.Contains("press 110", log);
        Assert.Contains("motion 25,17", log);
        Assert.Equal("release 110", log[^1]);
        Assert.Equal(1, log.Count(l => l.StartsWith("press", StringComparison.Ordinal)));

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void A_titlebar_touch_drag_moves_the_window_and_a_second_finger_joins_it()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(width: 200, height: 150, serverDecorated: true);
        var window = shell.Windows[0];
        var before = window.FrameBox;
        var titleX = before.X + (before.Width / 2);
        var titleY = before.Y + (window.Insets.Top / 2);

        Touch(shell, BasinViewInputKind.TouchDown, 5, titleX, titleY);
        Assert.True(shell.TouchGestureActive, "a titlebar touch began no gesture");
        Touch(shell, BasinViewInputKind.TouchMotion, 5, titleX + 40, titleY + 30);
        Assert.Equal((before.X + 40, before.Y + 30), (window.FrameBox.X, window.FrameBox.Y));

        Touch(shell, BasinViewInputKind.TouchDown, 6, titleX + 100, titleY + 30);
        Touch(shell, BasinViewInputKind.TouchMotion, 5, titleX + 60, titleY + 30);
        Assert.Equal((before.X + 50, before.Y + 30), (window.FrameBox.X, window.FrameBox.Y));
        Touch(shell, BasinViewInputKind.TouchUp, 5, titleX + 60, titleY + 30);
        Assert.True(shell.TouchGestureActive, "lifting one of two fingers ended the gesture");
        Touch(shell, BasinViewInputKind.TouchUp, 6, titleX + 100, titleY + 30);
        Assert.False(shell.TouchGestureActive);
        harness.PumpInput();
        Assert.Equal((before.X + 50, before.Y + 30), (window.FrameBox.X, window.FrameBox.Y));

        Move(shell, titleX + 200, titleY + 200);
        Assert.Equal((before.X + 50, before.Y + 30), (window.FrameBox.X, window.FrameBox.Y));

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void A_long_press_on_a_titlebar_opens_the_window_menu()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(width: 200, height: 150, serverDecorated: true);
        var window = shell.Windows[0];
        var before = window.FrameBox;
        var titleX = before.X + (before.Width / 2);
        var titleY = before.Y + (window.Insets.Top / 2);

        Touch(shell, BasinViewInputKind.TouchDown, 2, titleX, titleY);
        Touch(shell, BasinViewInputKind.TouchMotion, 2, titleX + 3, titleY + 2);
        shell.Tick(Environment.TickCount64 + 200);
        Assert.False(window.Frame!.IsMenuOpen, "the menu opened before the long-press delay");
        shell.Tick(Environment.TickCount64 + 600);
        Assert.True(window.Frame.IsMenuOpen, "the long press opened no menu");
        Assert.False(shell.TouchGestureActive);
        Assert.Equal((before.X, before.Y), (window.FrameBox.X, window.FrameBox.Y));

        Touch(shell, BasinViewInputKind.TouchMotion, 2, titleX + 40, titleY + 40);
        Touch(shell, BasinViewInputKind.TouchUp, 2, titleX + 40, titleY + 40);
        Assert.Equal((before.X, before.Y), (window.FrameBox.X, window.FrameBox.Y));
        Assert.True(window.Frame.IsMenuOpen, "the lift after the long press closed the menu");

        Touch(shell, BasinViewInputKind.TouchDown, 7, before.Right + 50, before.Bottom + 50);
        Touch(shell, BasinViewInputKind.TouchUp, 7, before.Right + 50, before.Bottom + 50);
        Assert.False(window.Frame.IsMenuOpen, "a touch outside the menu left it open");

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void A_long_press_on_a_client_is_a_right_click()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var pointer = harness.Client.Seat!.GetPointer();
        var touch = harness.Client.Seat.GetTouch();
        var buttons = new List<(uint Button, bool Pressed)>();
        var touches = new List<string>();
        pointer.Button += (_, e) => buttons.Add((e.Button, e.State == WlPointer.ButtonState.Pressed));
        touch.Down += (_, e) => touches.Add($"down {e.Id}");
        touch.Cancel += (_, _) => touches.Add("cancel");
        touch.Up += (_, e) => touches.Add($"up {e.Id}");
        harness.Pump();
        var toplevel = harness.MapToplevel(width: 200, height: 150);
        var client = shell.Windows[0].ClientBox;

        Touch(shell, BasinViewInputKind.TouchDown, 4, client.X + 20, client.Y + 20);
        shell.Tick(Environment.TickCount64 + 600);
        Touch(shell, BasinViewInputKind.TouchUp, 4, client.X + 20, client.Y + 20);
        harness.PumpUntil(() => buttons.Count == 2, "the right click never reached the client");
        Assert.Equal([(BtnRight, true), (BtnRight, false)], buttons);
        Assert.Equal(["down 4", "cancel"], touches);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void A_long_press_through_the_emulated_pointer_releases_the_left_button_first()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var pointer = harness.Client.Seat!.GetPointer();
        var buttons = new List<(uint Button, bool Pressed)>();
        pointer.Button += (_, e) => buttons.Add((e.Button, e.State == WlPointer.ButtonState.Pressed));
        harness.Pump();
        var toplevel = harness.MapToplevel(width: 200, height: 150);
        var client = shell.Windows[0].ClientBox;

        Touch(shell, BasinViewInputKind.TouchDown, 4, client.X + 20, client.Y + 20);
        shell.Tick(Environment.TickCount64 + 600);
        Touch(shell, BasinViewInputKind.TouchUp, 4, client.X + 20, client.Y + 20);
        harness.PumpUntil(() => buttons.Count == 4, "the right click never reached the client");
        Assert.Equal([(BtnLeft, true), (BtnLeft, false), (BtnRight, true), (BtnRight, false)], buttons);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void A_cancel_ends_a_drag_without_a_drop_and_reaches_a_client_as_a_cancel()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var touch = harness.Client.Seat!.GetTouch();
        var log = new List<string>();
        touch.Down += (_, e) => log.Add($"down {e.Id}");
        touch.Up += (_, e) => log.Add($"up {e.Id}");
        touch.Cancel += (_, _) => log.Add("cancel");
        harness.Pump();
        var toplevel = harness.MapToplevel(width: 200, height: 150, serverDecorated: true);
        var window = shell.Windows[0];
        var before = window.FrameBox;
        var titleX = before.X + (before.Width / 2);
        var titleY = before.Y + (window.Insets.Top / 2);

        Touch(shell, BasinViewInputKind.TouchDown, 1, titleX, titleY);
        Touch(shell, BasinViewInputKind.TouchMotion, 1, titleX + 40, titleY + 30);
        Assert.Equal((before.X + 40, before.Y + 30), (window.FrameBox.X, window.FrameBox.Y));
        Touch(shell, BasinViewInputKind.TouchCancel, 1, titleX + 40, titleY + 30);
        Assert.False(shell.TouchGestureActive);
        Assert.Equal((before.X, before.Y), (window.FrameBox.X, window.FrameBox.Y));
        Touch(shell, BasinViewInputKind.TouchMotion, 1, titleX + 80, titleY + 80);
        Touch(shell, BasinViewInputKind.TouchUp, 1, titleX + 80, titleY + 80);
        Assert.Equal((before.X, before.Y), (window.FrameBox.X, window.FrameBox.Y));

        var client = window.ClientBox;
        Touch(shell, BasinViewInputKind.TouchDown, 2, client.X + 10, client.Y + 10);
        harness.PumpUntil(() => log.Count == 1, "the touch down never reached the client");
        Touch(shell, BasinViewInputKind.TouchCancel, 2, client.X + 10, client.Y + 10);
        harness.PumpUntil(() => log.Count == 2, "the cancel never reached the client");
        Assert.Equal(["down 2", "cancel"], log);
        Touch(shell, BasinViewInputKind.TouchUp, 2, client.X + 10, client.Y + 10);
        harness.PumpInput();
        Assert.Equal(["down 2", "cancel"], log);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void A_client_move_request_on_a_touch_serial_drags_from_that_contact()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var touch = harness.Client.Seat!.GetTouch();
        var log = new List<string>();
        uint downSerial = 0;
        touch.Down += (_, e) =>
        {
            downSerial = e.Serial;
            log.Add($"down {e.Id}");
        };
        touch.Cancel += (_, _) => log.Add("cancel");
        harness.Pump();
        var toplevel = harness.MapToplevel(width: 200, height: 150);
        var window = shell.Windows[0];
        var before = window.FrameBox;
        var client = window.ClientBox;

        Touch(shell, BasinViewInputKind.TouchDown, 9, client.X + 30, client.Y + 40);
        harness.PumpUntil(() => log.Count == 1, "the touch down never reached the client");
        toplevel.Toplevel.Move(harness.Client.Seat, downSerial);
        harness.PumpUntil(() => shell.TouchGestureActive, "the move request began no touch gesture");
        harness.PumpUntil(() => log.Count == 2, "the client kept its touch after the move began");
        Assert.Equal(["down 9", "cancel"], log);

        Touch(shell, BasinViewInputKind.TouchMotion, 9, client.X + 50, client.Y + 70);
        Assert.Equal((before.X + 20, before.Y + 30), (window.FrameBox.X, window.FrameBox.Y));
        Touch(shell, BasinViewInputKind.TouchUp, 9, client.X + 50, client.Y + 70);
        Assert.False(shell.TouchGestureActive);
        harness.PumpInput();
        Assert.Equal((before.X + 20, before.Y + 30), (window.FrameBox.X, window.FrameBox.Y));

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void Touch_motion_and_the_tick_allocate_nothing_after_warm_up()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var desktopX = shell.WorkArea.Right - 10;
        var desktopY = shell.WorkArea.Bottom - 10;

        void DesktopCycle(int i)
        {
            Touch(shell, BasinViewInputKind.TouchDown, 2, desktopX, desktopY);
            Touch(shell, BasinViewInputKind.TouchMotion, 2, desktopX - (i % 5), desktopY);
            shell.Tick(Environment.TickCount64);
            Touch(shell, BasinViewInputKind.TouchUp, 2, desktopX, desktopY);
        }

        AssertFlat("an emulated pointer over the desktop", DesktopCycle);

        var toplevel = harness.MapToplevel(width: 200, height: 150, serverDecorated: true);
        var window = shell.Windows[0];
        var frame = window.FrameBox;
        var titleX = frame.X + (frame.Width / 2);
        var titleY = frame.Y + (window.Insets.Top / 2);

        void TitleCycle(int i)
        {
            Touch(shell, BasinViewInputKind.TouchDown, 1, titleX, titleY);
            Touch(shell, BasinViewInputKind.TouchMotion, 1, titleX + 1 + (i % 3), titleY);
            shell.Tick(Environment.TickCount64);
            Touch(shell, BasinViewInputKind.TouchMotion, 1, titleX, titleY);
            Touch(shell, BasinViewInputKind.TouchUp, 1, titleX, titleY);
        }

        AssertFlat("a titlebar touch gesture", TitleCycle);
        harness.PumpInput();
        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    private static void AssertFlat(string what, Action<int> cycle)
    {
        for (var i = 0; i < 50; i++)
        {
            cycle(i);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            cycle(i);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"{what} allocated {allocated} bytes over 1000 cycles");
    }
}
