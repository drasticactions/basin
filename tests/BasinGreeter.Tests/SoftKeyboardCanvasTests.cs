using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Basin.SoftKeyboard;
using Basin.SoftKeyboard.Avalonia;
using Xunit;

namespace BasinGreeter.Tests;

public sealed class SoftKeyboardCanvasTests
{
    private const float Padding = 5;

    private sealed class CountingSkin : ISoftKeyboardSkin
    {
        public int Backgrounds;
        public int Keys;
        public int Overs;
        public int Popups;
        public int PressedKeys;

        public void DrawBackground(DrawingContext context, Rect bounds) => Backgrounds++;

        public void DrawKey(DrawingContext context, in SoftKeyDraw key)
        {
            Keys++;
            PressedKeys += key.Pressed ? 1 : 0;
        }

        public void DrawOverKeys(DrawingContext context, Rect bounds) => Overs++;

        public void DrawPopup(DrawingContext context, in SoftKeyPopupDraw popup) => Popups++;

        public void Reset() => Backgrounds = Keys = Overs = Popups = PressedKeys = 0;
    }

    private static (Window Window, SoftKeyboardCanvas Canvas, SoftKeyboardEngine Engine, List<TypedKey> Typed) Show(ISoftKeyboardSkin skin, SoftKeyboardInput input = SoftKeyboardInput.Pointer)
    {
        var engine = new SoftKeyboardEngine(KeyboardLayouts.Load("us-qwerty")!.Grid());
        engine.Layout(1024, 330, Padding);
        var typed = new List<TypedKey>();
        engine.Typed += typed.Add;
        var canvas = new SoftKeyboardCanvas { Engine = engine, Skin = skin, Input = input, Width = 1024, Height = 335 };
        var window = new Window { Content = canvas, Width = 1024, Height = 335, SizeToContent = SizeToContent.Manual };
        window.Show();
        _ = window.CaptureRenderedFrame();
        return (window, canvas, engine, typed);
    }

    private static Point CenterOf(SoftKeyboardEngine engine, string text)
    {
        for (var cell = 0; cell < KeyGrid.DefaultRows * KeyGrid.DefaultColumns; cell++)
        {
            if (engine.Grid[cell] is { IsVisible: true, Plain: { IsText: true } plain } && plain.Text == text)
            {
                var rect = engine.Rects[cell];
                return new Point(rect.CenterX, rect.CenterY + Padding);
            }
        }

        throw new InvalidOperationException($"no key {text}");
    }

    private static int CellOf(SoftKeyboardEngine engine, string text)
    {
        for (var cell = 0; cell < KeyGrid.DefaultRows * KeyGrid.DefaultColumns; cell++)
        {
            if (engine.Grid[cell] is { IsVisible: true, Plain: { IsText: true } plain } && plain.Text == text)
            {
                return cell;
            }
        }

        return -1;
    }

    [AvaloniaFact]
    public void Pointer_mode_presses_and_releases_a_key()
    {
        var skin = new CountingSkin();
        var (window, _, engine, typed) = Show(skin);
        var at = CenterOf(engine, "g");

        skin.Reset();
        window.MouseDown(at, MouseButton.Left);
        Assert.True(engine.IsPressed(CellOf(engine, "g")));
        _ = window.CaptureRenderedFrame();
        Assert.True(skin.Backgrounds >= 1);
        Assert.Equal(skin.Backgrounds, skin.PressedKeys);

        window.MouseUp(at, MouseButton.Left);

        Assert.Equal("g", string.Concat(typed.Select(static t => t.Text)));
        Assert.False(engine.IsPressed(CellOf(engine, "g")));
        window.Close();
    }

    [AvaloniaFact]
    public void Losing_the_capture_cancels_the_touch()
    {
        var skin = new CountingSkin();
        var (window, canvas, engine, typed) = Show(skin);
        var ended = 0;
        canvas.TouchesEnded += () => ended++;
        var at = CenterOf(engine, "h");

        window.MouseDown(at, MouseButton.Left);
        Assert.True(engine.IsPressed(CellOf(engine, "h")));
        window.Content = null;

        Assert.False(engine.IsPressed(CellOf(engine, "h")));
        Assert.Empty(typed);
        Assert.Equal(1, ended);
        window.Close();
    }

    [AvaloniaFact]
    public void External_mode_takes_no_input()
    {
        var skin = new CountingSkin();
        var (window, canvas, engine, typed) = Show(skin, SoftKeyboardInput.External);
        var at = CenterOf(engine, "j");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Assert.False(canvas.IsHitTestVisible);
        Assert.Empty(typed);
        engine.TapCell(1, CellOf(engine, "j"), Environment.TickCount64);
        Assert.Equal("j", string.Concat(typed.Select(static t => t.Text)));
        window.Close();
    }

    [AvaloniaFact]
    public void A_frame_calls_the_skin_once_per_visible_key_and_the_popup_when_one_is_open()
    {
        var skin = new CountingSkin();
        var (window, canvas, engine, _) = Show(skin);
        var visible = 0;
        for (var cell = 0; cell < KeyGrid.DefaultRows * KeyGrid.DefaultColumns; cell++)
        {
            visible += engine.Grid[cell] is { IsVisible: true, Plain: not null } ? 1 : 0;
        }

        skin.Reset();
        engine.CloseLongPress();
        canvas.InvalidateVisual();
        _ = window.CaptureRenderedFrame();
        Assert.Equal((1, visible, 1, 0), (skin.Backgrounds, skin.Keys, skin.Overs, skin.Popups));

        Assert.True(engine.OpenLongPress(CellOf(engine, "e")));
        skin.Reset();
        canvas.InvalidateVisual();
        _ = window.CaptureRenderedFrame();
        Assert.Equal((1, visible, 1, 1), (skin.Backgrounds, skin.Keys, skin.Overs, skin.Popups));
        window.Close();
    }

    [AvaloniaFact]
    public void The_labels_follow_the_shift_state()
    {
        var engine = new SoftKeyboardEngine(KeyboardLayouts.Load("us-qwerty")!.Grid());
        engine.Layout(1024, 330, Padding);
        var cap = engine.CapAt(CellOf(engine, "q"))!;

        Assert.Equal("q", SoftKeyLabels.For(engine, cap));
        engine.State.TapShift(Environment.TickCount64);
        Assert.Equal("Q", SoftKeyLabels.For(engine, engine.CapAt(CellOf(engine, "q"))!));
    }

    [AvaloniaFact]
    public void The_canvas_allocates_nothing_per_frame_after_warm_up()
    {
        var skin = new CountingSkin();
        var (window, canvas, engine, _) = Show(skin);
        engine.TouchDown(5, engine.Rects[CellOf(engine, "k")].CenterX, engine.Rects[CellOf(engine, "k")].CenterY, Environment.TickCount64);
        engine.Frame(Environment.TickCount64);
        using var target = new RenderTargetBitmap(new PixelSize(1024, 335));
        using (var context = target.CreateDrawingContext())
        {
            for (var warm = 0; warm < 10; warm++)
            {
                canvas.Render(context);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var frame = 0; frame < 200; frame++)
            {
                canvas.Render(context);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.True(skin.Keys > 0);
        window.Close();
    }

    [AvaloniaFact]
    public void A_cached_skin_lays_out_each_label_once()
    {
        var cache = new SoftKeyTextCache(FontFamily.Default, capacity: 8);
        var first = cache.Get("q", 22, Colors.White);

        Assert.Same(first, cache.Get("q", 22, Colors.White));
        Assert.NotSame(first, cache.Get("q", 22, Colors.White, FontWeight.Bold));
        Assert.NotSame(first, cache.Get("q", 21, Colors.White));
        Assert.Equal(3, cache.Count);
        for (var i = 0; i < 8; i++)
        {
            _ = cache.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture), 22, Colors.White);
        }

        Assert.True(cache.Count <= cache.Capacity);
        cache.Clear();
        Assert.Equal(0, cache.Count);
    }
}
