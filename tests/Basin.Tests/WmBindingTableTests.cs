using Basin.Config;
using Xunit;
using Wm = Basin.WindowManager;

namespace Basin.Tests;

public class WmBindingTableTests
{
    private enum Mode
    {
        Normal,
    }

    [Fact]
    public void A_reset_table_leaves_no_destroyed_binding_for_a_later_submap()
    {
        using var fixture = new RiverFixture();
        var seat = Assert.Single(fixture.Seats);
        var table = new Wm.WmBindingTable<Mode>(fixture.Client);
        var bindings = fixture.Client.Bindings;

        fixture.OnManage = context => table.Arm(context, s =>
        {
            table.Bind(s, "q", Modifiers.Super, () => { });
            table.Bind(s, "w", Modifiers.Super, () => { });
        });
        fixture.RequestManageAndSettle();
        Assert.Equal(2, bindings.KeyBindings.Count);

        Wm.KeyBinding? fresh = null;
        Wm.WmSubmap? submap = null;
        fixture.OnManage = context =>
        {
            table.Apply(locked: false);
            table.Reset();
            fresh = bindings.Bind(seat, "e", Modifiers.Super);
            submap = bindings.EnterSubmap(seat, [fresh], TimeSpan.Zero);
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        Assert.NotNull(submap);
        Assert.Same(fresh, Assert.Single(bindings.KeyBindings));
        Assert.All(bindings.KeyBindings, b => Assert.False(b.IsDestroyed));
    }

    [Fact]
    public void A_destroyed_binding_throws_a_clear_error_and_a_second_destroy_does_nothing()
    {
        using var fixture = new RiverFixture();
        var seat = Assert.Single(fixture.Seats);

        Exception? error = null;
        fixture.OnManage = _ =>
        {
            var binding = fixture.Client.Bindings.Bind(seat, "q", Modifiers.Super);
            binding.Enable();
            binding.Destroy();
            binding.Destroy();
            Assert.True(binding.IsDestroyed);
            Assert.False(binding.IsEnabled);
            error = Record.Exception(binding.Enable);
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        var disposed = Assert.IsType<ObjectDisposedException>(error);
        Assert.Contains("Enable", disposed.Message);
    }

    [Fact]
    public void Exiting_a_submap_skips_a_binding_that_was_destroyed_while_suspended()
    {
        using var fixture = new RiverFixture();
        var seat = Assert.Single(fixture.Seats);
        var bindings = fixture.Client.Bindings;

        Wm.KeyBinding? normal = null;
        Wm.WmSubmap? submap = null;
        fixture.OnManage = _ =>
        {
            normal = bindings.Bind(seat, "q", Modifiers.Super);
            normal.Enable();
            var inner = bindings.Bind(seat, "e", Modifiers.Super);
            submap = bindings.EnterSubmap(seat, [inner], TimeSpan.Zero);
        };
        fixture.RequestManageAndSettle();

        Exception? error = null;
        fixture.OnManage = _ =>
        {
            normal!.Destroy();
            error = Record.Exception(submap!.Exit);
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        Assert.Null(error);
        Assert.True(submap!.IsExited);
    }

    [Fact]
    public void Exiting_a_submap_removes_its_manage_handler()
    {
        using var fixture = new RiverFixture();
        var seat = Assert.Single(fixture.Seats);
        var bindings = fixture.Client.Bindings;

        Wm.KeyBinding? inner = null;
        var before = 0;
        fixture.OnManage = _ =>
        {
            inner = bindings.Bind(seat, "e", Modifiers.Super);
            before = fixture.Client.ManageHandlerCount;
            for (var i = 0; i < 100; i++)
            {
                bindings.EnterSubmap(seat, [inner], TimeSpan.Zero).Exit();
            }
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        Assert.Equal(before, fixture.Client.ManageHandlerCount);
    }

    [Fact]
    public void An_unknown_key_name_throws_instead_of_binding_keysym_zero()
    {
        using var fixture = new RiverFixture();
        var seat = Assert.Single(fixture.Seats);
        var table = new Wm.WmBindingTable<Mode>(fixture.Client);

        Exception? error = null;
        fixture.OnManage = _ => error ??= Record.Exception(() => table.Bind(seat, "NoSuchKey", Modifiers.Super, () => { }));
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        Assert.IsType<ArgumentException>(error);
        Assert.Empty(fixture.Client.Bindings.KeyBindings);
    }

    [Fact]
    public void A_second_reload_signal_dispose_leaves_a_reused_fd_open()
    {
        using var fixture = new RiverFixture();
        var reload = new Wm.WmReloadSignal(fixture.Client);
        reload.Dispose();

        var fds = new int[2];
        Assert.Equal(0, Pipe(fds));
        try
        {
            reload.Dispose();
            Assert.NotEqual(-1, Fcntl(fds[0], 1));
            Assert.NotEqual(-1, Fcntl(fds[1], 1));
        }
        finally
        {
            _ = Close(fds[0]);
            _ = Close(fds[1]);
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "pipe", SetLastError = true)]
    private static extern int Pipe(int[] fds);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int fd, int command);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);
}
