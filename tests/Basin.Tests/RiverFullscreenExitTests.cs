using Basin.Shell.Xdg.Protocol;
using Xunit;
using Wm = Basin.WindowManager;

namespace Basin.Tests;

public sealed class RiverFullscreenExitTests
{
    [Fact]
    public void Exit_fullscreen_and_proposed_dimensions_in_one_sequence_reach_the_client_as_one_configure()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();
        var seen = Record(toplevel);
        Fullscreen(fixture);

        seen.Clear();
        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.ExitFullscreen();
                window.InformFullscreen(false);
                window.ProposeDimensions(90, 70);
            }
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        var configure = Assert.Single(seen);
        Assert.Equal((90, 70, false), configure);
    }

    [Fact]
    public void Exit_fullscreen_with_no_new_proposal_restores_the_last_proposed_dimensions_in_one_configure()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();
        var seen = Record(toplevel);
        Fullscreen(fixture);

        seen.Clear();
        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.ExitFullscreen();
                window.InformFullscreen(false);
            }
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        var configure = Assert.Single(seen);
        Assert.Equal((90, 70, false), configure);
    }

    [Fact]
    public void A_session_floats_a_fullscreen_window_and_tiles_it_again_with_no_windowed_size()
    {
        using var fixture = new RiverFixture();
        var session = Session(fixture);
        var toplevel = fixture.MapToplevel();
        var seen = Record(toplevel);
        Fullscreen(fixture);
        var full = seen[^1];
        Assert.True(full.Fullscreen);

        seen.Clear();
        Sequence(fixture, session, managed => Assert.True(session.Float(managed, fullscreen: true)));
        Assert.NotEmpty(seen);
        Assert.False(seen[^1].Fullscreen);
        Assert.True(session.RefullscreenOnTile(session.Windows[0]));

        Sequence(fixture, session, managed =>
        {
            Assert.True(session.Tile(managed, fullscreen: false));
            managed.Window.Fullscreen(fixture.Outputs[0]);
            managed.Window.InformFullscreen(true);
        });

        Assert.True(seen[^1].Fullscreen);
        Assert.All(seen, configure => Assert.Equal((full.Width, full.Height), (configure.Width, configure.Height)));
    }

    [Fact]
    public void A_client_that_leaves_fullscreen_while_floated_clears_the_session_record()
    {
        using var fixture = new RiverFixture();
        var session = Session(fixture);
        var toplevel = fixture.MapToplevel();
        Fullscreen(fixture);
        Sequence(fixture, session, managed => session.Float(managed, fullscreen: true));

        toplevel.Toplevel.UnsetFullscreen();
        Sequence(fixture, session, _ => { });

        Assert.False(session.RefullscreenOnTile(session.Windows[0]));
        Sequence(fixture, session, managed => Assert.False(session.Tile(managed, fullscreen: false)));
    }

    [Fact]
    public void A_session_fullscreen_exit_reaches_the_client_as_one_configure_with_the_restored_size()
    {
        using var fixture = new RiverFixture();
        var session = Session(fixture);
        var toplevel = fixture.MapToplevel();
        var seen = Record(toplevel);
        Fullscreen(fixture);

        seen.Clear();
        Sequence(fixture, session, managed => session.ExitFullscreen(managed, new Wm.Size(80, 60)));

        var configure = Assert.Single(seen);
        Assert.Equal((80, 60, false), configure);
    }

    private static Wm.WmSession<Managed> Session(RiverFixture fixture)
    {
        var session = new Wm.WmSession<Managed>(new Wm.WmFocusStack<Managed>(fixture.Client), window => new Managed(window));
        fixture.OnManage = session.AdoptNewWindows;
        return session;
    }

    private static void Sequence(RiverFixture fixture, Wm.WmSession<Managed> session, Action<Managed> act)
    {
        fixture.OnManage = context =>
        {
            session.AdoptNewWindows(context);
            session.ForgetClosedWindows(context);
            foreach (var managed in session.Windows)
            {
                act(managed);
            }
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
    }

    private sealed class Managed(Wm.WmWindow window) : Wm.IWmManagedWindow
    {
        public Wm.WmWindow Window { get; } = window;
    }

    private static void Fullscreen(RiverFixture fixture)
    {
        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.ProposeDimensions(90, 70);
            }
        };
        fixture.RequestManageAndSettle();

        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.Fullscreen(context.Outputs[0]);
                window.InformFullscreen(true);
            }
        };
        fixture.RequestManageAndSettle();
        fixture.Settle();
        fixture.OnManage = null;
    }

    private static List<(int Width, int Height, bool Fullscreen)> Record(MappedToplevel toplevel)
    {
        var seen = new List<(int Width, int Height, bool Fullscreen)>();
        toplevel.Toplevel.Configure += (_, e) =>
        {
            var fullscreen = false;
            for (var offset = 0; offset + 4 <= e.States.Length; offset += 4)
            {
                fullscreen |= BitConverter.ToUInt32(e.States, offset) == (uint)XdgToplevel.State.Fullscreen;
            }

            seen.Add((e.Width, e.Height, fullscreen));
        };
        return seen;
    }
}
