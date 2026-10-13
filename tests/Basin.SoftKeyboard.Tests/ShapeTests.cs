using Basin.Desktop;
using Basin.Desktop.Protocol;
using Basin.Tests;
using Xunit;

namespace Basin.SoftKeyboard.Tests;

public sealed class ShapeTests
{
    private const string SixBySixteen = """
        id = "six"
        name = "Six"
        width_units = 16
        grid_rows = 6
        grid_columns = 16
        row_heights = [40, 60, 60, 60, 60, 60]
        rows = [
            [{ plain = "{escape}" }, { plain = "{f1}" }, { plain = "{f2}" }, { w = 13, spacer = true }],
            [{ plain = "1" }, { plain = "2" }, { plain = "3" }, { plain = "4" }, { plain = "5" }, { plain = "6" }, { plain = "7" }, { plain = "8" }, { plain = "9" }, { plain = "0" }, { plain = "-" }, { plain = "=" }, { w = 4, plain = "{backspace}" }],
            [{ plain = "{tab}" }, { plain = "q" }, { plain = "w" }, { plain = "e" }, { plain = "r" }, { plain = "t" }, { w = 2, spacer = true }, { plain = "y" }, { plain = "u" }, { plain = "i" }, { plain = "o" }, { plain = "p" }, { w = 3, plain = "{delete}" }],
            [{ plain = "a" }, { plain = "s" }, { plain = "d" }, { plain = "f" }, { plain = "g" }, { plain = "h" }, { plain = "j" }, { plain = "k" }, { plain = "l" }, { w = 7, plain = "{return}" }],
            [{ plain = "{shift}" }, { plain = "z" }, { plain = "x" }, { plain = "c" }, { plain = "v" }, { plain = "b" }, { plain = "n" }, { plain = "m" }, { plain = "{up}" }, { w = 7, plain = "{shift}" }],
            [{ plain = "{control}" }, { plain = "{super}" }, { plain = "{alt}" }, { w = 8, plain = " " }, { plain = "{left}" }, { plain = "{down}" }, { plain = "{right}" }, { plain = "{page:symbols}", label = "&123" }, { plain = "{layout-switch}" }],
        ]

        [pages.symbols]
        rows = [
            [{ w = 16, spacer = true }],
            [{ plain = "!" }, { plain = "@" }, { w = 14, spacer = true }],
            [{ w = 16, spacer = true }],
            [{ w = 16, spacer = true }],
            [{ w = 16, spacer = true }],
            [{ w = 15, spacer = true }, { plain = "{page:symbols}", label = "abc" }],
        ]
        """;

    private static KeyboardLayout Six() => KeyboardLayouts.Parse(SixBySixteen, "six");

    private static SoftKeyboardEngine Engine(KeyboardLayout layout)
    {
        var engine = new SoftKeyboardEngine(layout.Grid());
        engine.Pages = layout.PageSet();
        engine.Layout(1600, 340, 5);
        return engine;
    }

    [Fact]
    public void A_layout_file_names_its_own_rows_columns_and_row_heights()
    {
        var layout = Six();
        var grid = layout.Grid();
        Assert.True(layout.IsShaped);
        Assert.Equal(6, grid.Rows);
        Assert.Equal(16, grid.Columns);
        Assert.Equal(96, grid.CellCount);
        Assert.False(grid.RowBias);
        Assert.Equal([40f, 60, 60, 60, 60, 60], grid.RowHeights);
        for (var row = 0; row < grid.Rows; row++)
        {
            Assert.Equal(16, grid.RowWeight(row), 3);
        }

        Assert.True(grid[0, 3].IsSpacer);
        Assert.False(grid[0, 3].IsKey);
        Assert.False(grid[0, 3].IsVisible);
        Assert.Equal(KeyFunction.FunctionKey, grid[0, 1].Plain!.Function);
        Assert.Equal(1, grid[0, 1].Plain!.Argument);
        Assert.Equal("&123", grid[5, 7].Plain!.Label);
        Assert.Equal("symbols", grid[5, 7].Plain!.Target);
        Assert.True(Keys.Layout().Grid().RowBias);
        Assert.Equal(KeyGrid.DefaultRows, Keys.Layout().Grid().Rows);
    }

    [Fact]
    public void A_touch_on_a_spacer_goes_nowhere_and_the_keys_beside_it_keep_their_width()
    {
        var engine = Engine(Six());
        var grid = engine.Grid;
        var spacer = engine.Rects[grid.Index(2, 6)];
        Assert.Equal(-1, engine.HitTester.Find(spacer.CenterX, spacer.CenterY));
        Assert.Equal(-1, engine.HitTester.Find(spacer.X + 2, spacer.CenterY));
        var t = engine.Rects[grid.Index(2, 5)];
        var y = engine.Rects[grid.Index(2, 7)];
        Assert.Equal(grid.Index(2, 5), engine.HitTester.Find(t.Right - 1, t.CenterY));
        Assert.Equal(grid.Index(2, 7), engine.HitTester.Find(y.X + 1, y.CenterY));
        Assert.Equal(100, t.Width, 2);
        Assert.Equal(200, spacer.Width, 2);
        var typed = Keys.Record(engine);
        engine.TouchDown(1, spacer.CenterX, spacer.CenterY, 0);
        engine.Frame(0);
        engine.TouchUp(1, 10);
        Assert.Empty(typed);
    }

    [Fact]
    public void A_page_key_swaps_the_grid_and_the_same_key_on_the_page_comes_back()
    {
        var engine = Engine(Six());
        var pages = new List<string>();
        engine.PageChanged += pages.Add;
        engine.TapCell(1, engine.Grid.Index(5, 7), 0);
        Assert.Equal("symbols", engine.Page);
        Assert.Equal("@", engine.Grid[1, 1].Plain!.Text);
        var typed = Keys.Record(engine);
        engine.TapCell(1, engine.Grid.Index(1, 1), 100);
        Assert.Equal("@", Assert.Single(typed).Text);
        Assert.Equal("symbols", engine.Page);
        engine.TapCell(1, engine.Grid.Index(5, 1), 200);
        Assert.Equal(KeyPageSet.MainPage, engine.Page);
        Assert.Equal(["symbols", KeyPageSet.MainPage], pages);
    }

    [Fact]
    public void The_layout_switch_key_asks_for_the_next_shape()
    {
        var engine = Engine(Six());
        var asked = new List<string?>();
        engine.ShapeRequested += asked.Add;
        engine.TapCell(1, engine.Grid.Index(5, 8), 0);
        Assert.Equal([null], asked);
        Assert.True(KeyCap.TryParse("{shape:thumb}", out var cap));
        Assert.Equal(KeyFunction.ShapeChoice, cap!.Function);
        Assert.Equal("thumb", cap.Target);
    }

    [Fact]
    public void Switching_between_shapes_and_pages_allocates_nothing()
    {
        var six = Six().PageSet();
        var five = new KeyPageSet(Keys.Layout().Grid());
        var engine = new SoftKeyboardEngine(five.Main);
        engine.Layout(1600, 340, 5);
        engine.Pages = six;
        engine.Pages = five;
        long now = 0;

        void Round()
        {
            engine.Pages = six;
            engine.ShowPage("symbols");
            engine.TapCell(1, engine.Grid.Index(1, 1), now += 10);
            engine.ShowPage(KeyPageSet.MainPage);
            engine.TapCell(1, engine.Grid.Index(2, 1), now += 10);
            engine.Pages = five;
            engine.TapCell(1, engine.Grid.Index(1, 1), now += 10);
        }

        for (var i = 0; i < 20; i++)
        {
            Round();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            Round();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Control_latches_for_one_key_locks_on_a_double_tap_and_clears_on_a_tap_while_locked()
    {
        var engine = Engine(Six());
        var typed = Keys.Record(engine);
        engine.State.SetAutoCap(true);
        Keys.Tap(engine, "control", 0);
        Assert.Equal(ShiftMode.Once, engine.State.Control);
        Keys.Tap(engine, "c", 100);
        Assert.Equal(KeyModifiers.Control, typed[^1].Modifiers);
        Assert.Equal("c", typed[^1].Text);
        Assert.Equal(ShiftMode.Off, engine.State.Control);
        Keys.Tap(engine, "v", 200);
        Assert.Equal(KeyModifiers.None, typed[^1].Modifiers);

        Keys.Tap(engine, "control", 1000);
        Keys.Tap(engine, "control", 1200);
        Assert.Equal(ShiftMode.Lock, engine.State.Control);
        Keys.Tap(engine, "a", 1300);
        Keys.Tap(engine, "x", 1400);
        Assert.All(typed[^2..], static t => Assert.Equal(KeyModifiers.Control, t.Modifiers));
        Keys.Tap(engine, "control", 1500);
        Assert.Equal(ShiftMode.Off, engine.State.Control);

        Keys.Tap(engine, "alt", 3000);
        Keys.Tap(engine, "super", 3100);
        Keys.Tap(engine, "tab", 3200);
        Assert.Equal(KeyModifiers.Alt | KeyModifiers.Super, typed[^1].Modifiers);
    }

    [Fact]
    public void Escape_the_arrows_and_the_function_keys_go_out_as_keys()
    {
        var output = new RecordingOutput();
        var router = new KeyboardRouter(new SoftKeyboardTextInput(), output);
        var engine = Engine(Six());
        long now = 0;
        router.Attach(engine, () => now);
        Keys.Tap(engine, "escape", now += 10);
        Keys.Tap(engine, "up", now += 10);
        Keys.Tap(engine, "down", now += 10);
        Keys.Tap(engine, "f2", now += 10);
        Keys.Tap(engine, "delete", now += 10);
        Assert.Equal("[1][103][108][60][111]", output.Log);
        Keys.Tap(engine, "shift", now += 1000);
        Keys.Tap(engine, "up", now += 10);
        Assert.EndsWith("[Shift+103]", output.Log);
        Assert.Equal((88u, 0xffc9u), KeyboardRouter.Keystroke(KeyCap.FunctionKeyCap(12)));
        Assert.Equal((59u, 0xffbeu), KeyboardRouter.Keystroke(KeyCap.FunctionKeyCap(1)));
    }

    [Fact]
    public void Control_and_c_reach_a_client_with_a_text_input_as_a_key_and_not_as_text()
    {
        using var host = new CompositorTestHost();
        var fallback = new SoftKeyboardTextInput();
        using var relay = new InputMethodRelay(host.Display, host.Seat, fallback);
        using var manager = new TextInputManager(host.Display, host.Seat, relay);
        var window = MappedToplevel.Map(host, host.Client);
        var registry = host.Client.Display.GetRegistry();
        ZwpTextInputManagerV3? bound = null;
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_text_input_manager_v3")
            {
                bound = registry.Bind<ZwpTextInputManagerV3>(e.Name, 1);
            }
        };
        host.PumpToClient();
        var textInput = bound!.GetTextInput(host.Client.Seat!);
        var commits = new List<string?>();
        var entered = 0;
        textInput.Enter += (_, _) => entered++;
        textInput.CommitString += (_, e) => commits.Add(e.Text);
        var output = new RecordingOutput();
        var router = new KeyboardRouter(fallback, output);
        var engine = Engine(Six());
        long now = 0;
        router.Attach(engine, () => now);
        manager.NotifyFocus(window.ServerSurface);
        host.PumpUntil(() => entered == 1);
        textInput.Enable();
        textInput.Commit();
        host.PumpToServer();
        Assert.Equal(KeyRoute.TextInput, router.Route);

        Keys.Tap(engine, "control", now += 10);
        Keys.Tap(engine, "c", now += 100);
        Keys.Tap(engine, "a", now += 100);
        host.PumpUntil(() => commits.Count == 1);
        Assert.Equal("[Control+99]", output.Log);
        Assert.Equal(["a"], commits);
    }

    [Fact]
    public void A_spacer_needs_a_weight_and_a_grid_has_a_bounded_shape()
    {
        Assert.Throws<ArgumentOutOfRangeException>(static () => KeyCell.Spacer(0));
        Assert.Throws<FormatException>(static () => KeyboardLayouts.Parse(
            "grid_rows = 2\ngrid_columns = 2\nrows = [[{ w = 0, spacer = true }], [{ plain = \"a\" }]]", "bad"));
        Assert.Throws<ArgumentOutOfRangeException>(static () => new KeyGrid(9, 4, new KeyCell[36]));
        Assert.Throws<ArgumentOutOfRangeException>(static () => new KeyGrid(2, 21, new KeyCell[42]));
        Assert.Throws<FormatException>(static () => KeyboardLayouts.Parse("grid_rows = 9\nrows = []", "bad"));
    }

    private sealed class RecordingOutput : ISoftKeyOutput
    {
        public string Log { get; private set; } = string.Empty;

        public void Key(uint keycode, uint timeMs) => Log += $"[{keycode}]";

        public void ControlKey(uint keycode, uint timeMs) => Log += $"[ctrl+{keycode}]";

        public void ModifiedKey(uint keycode, uint xkbModifiers, KeyModifiers modifiers, uint timeMs) =>
            Log += $"[{modifiers.ToString().Replace(", ", "+", StringComparison.Ordinal)}+{keycode}]";

        public int Type(string text, uint timeMs)
        {
            Log += text;
            return text.Length;
        }

        public bool TryResolve(uint keysym, out uint keycode, out uint modifiers)
        {
            keycode = keysym & 0xff;
            modifiers = 0;
            return true;
        }
    }
}
