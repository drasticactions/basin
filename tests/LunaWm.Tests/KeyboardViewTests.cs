using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Basin.SoftKeyboard;
using Xunit;

namespace LunaWm.Tests;

public sealed class KeyboardViewTests
{
    private static (Window Window, KeyboardModel Model) Host(KeyboardSize size)
    {
        var metrics = KeyboardMetrics.For(size, 1024, 768);
        var engine = new SoftKeyboardEngine(KeyboardLayouts.Load("us-qwerty")!.Grid());
        engine.Layout(1024, metrics.KeymapHeight, metrics.Padding);
        var model = new KeyboardModel(engine) { Width = 1024, Height = metrics.Height, Padding = metrics.Padding, Size = size };
        var view = new KeyboardView { Model = model };
        var window = new Window
        {
            Content = view,
            Width = 1024,
            Height = metrics.Height + KeyboardModel.PopupRoom,
            Background = Avalonia.Media.Brushes.Magenta,
        };
        window.Show();
        Pump();
        return (window, model);
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static (byte R, byte G, byte B) Pixel(Window window, double x, double y)
    {
        Pump();
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        unsafe
        {
            var pixel = (byte*)buffer.Address + ((int)y * buffer.RowBytes) + ((int)x * 4);
            return buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888
                ? (pixel[0], pixel[1], pixel[2])
                : (pixel[2], pixel[1], pixel[0]);
        }
    }

    private static Point FaceCenter(KeyboardModel model, string name)
    {
        var grid = model.Engine.Grid;
        for (var cell = 0; cell < KeyGrid.DefaultRows * KeyGrid.DefaultColumns; cell++)
        {
            if (grid[cell] is { IsVisible: true, Plain: { } plain } && (plain.IsText ? plain.Text : plain.Name) == name)
            {
                var rect = model.Engine.Rects[cell];
                return new Point(rect.X + 9, rect.Y + model.KeymapTop + 9);
            }
        }

        throw new InvalidOperationException(name);
    }

    private static void Near(byte expected, byte actual) => Assert.InRange(actual, expected - 4, expected + 4);

    [AvaloniaTheory]
    [InlineData(KeyboardSize.ExtraSmall)]
    [InlineData(KeyboardSize.Small)]
    [InlineData(KeyboardSize.Medium)]
    [InlineData(KeyboardSize.Large)]
    public void Each_size_draws_its_faces_inside_the_grid_rects(KeyboardSize size)
    {
        var (window, model) = Host(size);
        var letter = FaceCenter(model, "q");
        var (r, g, b) = Pixel(window, letter.X, letter.Y);
        Near(210, r);
        Near(210, g);
        Near(210, b);
        var function = FaceCenter(model, "tab");
        Near(99, Pixel(window, function.X, function.Y).R);
        var number = FaceCenter(model, "5");
        Near(169, Pixel(window, number.X, number.Y).R);
        var above = Pixel(window, 500, KeyboardModel.PopupRoom - 20);
        Assert.Equal((255, 0, 255), above);
        window.Close();
    }

    [AvaloniaFact]
    public void Shift_once_and_lock_have_their_own_faces()
    {
        var (window, model) = Host(KeyboardSize.Medium);
        var shift = FaceCenter(model, "shift");
        Near(99, Pixel(window, shift.X, shift.Y).R);
        model.Engine.State.TapShift(0);
        model.Invalidate();
        var once = Pixel(window, shift.X, shift.Y);
        Near(131, once.R);
        Near(139, once.B);
        model.Engine.State.TapShift(100);
        model.Invalidate();
        var locked = Pixel(window, shift.X, shift.Y);
        Near(75, locked.R);
        Near(198, locked.B);
        window.Close();
    }

    [AvaloniaFact]
    public void A_key_with_a_list_carries_the_ellipsis_mark()
    {
        var (window, model) = Host(KeyboardSize.Medium);
        int Dark(string name)
        {
            var rect = model.Engine.Rects[KeyIndex(model, name)];
            var count = 0;
            for (var x = (int)rect.Right - 24; x < (int)rect.Right - 14; x++)
            {
                for (var y = (int)(rect.Bottom + model.KeymapTop) - 14; y < (int)(rect.Bottom + model.KeymapTop) - 6; y++)
                {
                    if (Pixel(window, x, y).R < 120)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        Assert.True(Dark("e") > 2, "e has a long-press list and no mark");
        Assert.Equal(0, Dark("f"));
        window.Close();
    }

    [AvaloniaFact]
    public void An_open_long_press_list_draws_its_frame_above_the_key()
    {
        var (window, model) = Host(KeyboardSize.Medium);
        var engine = model.Engine;
        Assert.True(engine.OpenLongPress(KeyIndex(model, "e")));
        model.Invalidate();
        var frame = engine.LongPressGeometry.Frame;
        Assert.Equal(2, engine.LongPressGeometry.Lines);
        var body = Pixel(window, frame.X + 5, frame.Y + model.KeymapTop + 40);
        Assert.InRange(body.R, 40, 55);
        Assert.InRange(body.G, 25, 40);
        var cell = engine.LongPressGeometry.CellRect(0);
        Near(210, Pixel(window, cell.X + 10, cell.Y + model.KeymapTop + 14).R);
        window.Close();
    }

    [AvaloniaFact]
    public void The_candidate_bar_draws_the_preedit_underlined_and_the_first_word_blue()
    {
        var (window, model) = Host(KeyboardSize.Medium);
        model.BarHeight = KeyboardMetrics.CandidateBarHeight;
        model.Candidates = ["hello", "help", "held"];
        model.Preedit = "hel";
        model.Invalidate();
        var bar = Pixel(window, 1000, KeyboardModel.PopupRoom + 27);
        Assert.Equal((0, 0, 0), bar);
        Pump();
        Assert.Equal(3, model.CandidateSpans.Count);
        var first = model.CandidateSpans[0];
        var blue = 0;
        for (var x = (int)first.Left; x < (int)first.Right; x++)
        {
            for (var y = (int)KeyboardModel.PopupRoom + 10; y < (int)KeyboardModel.PopupRoom + 45; y++)
            {
                var (r, g, b) = Pixel(window, x, y);
                if (b > 150 && r < 120)
                {
                    blue++;
                }
            }
        }

        Assert.True(blue > 20, $"the first candidate is not blue ({blue})");
        Assert.Equal(1, model.CandidateAt(first.Left + 5) + 1);
        Assert.Equal(2, model.CandidateAt(model.CandidateSpans[2].Left + 5));
        var underline = 0;
        for (var y = (int)KeyboardModel.PopupRoom + 30; y < (int)KeyboardModel.PopupRoom + 50; y++)
        {
            if (Pixel(window, 30, y).R > 200)
            {
                underline++;
            }
        }

        Assert.True(underline > 0, "the preedit has no underline");
        window.Close();
    }

    [AvaloniaFact]
    public void A_trace_is_drawn_as_a_blue_stroke_over_the_keys()
    {
        var (window, model) = Host(KeyboardSize.Medium);
        var engine = model.Engine;
        engine.TraceEnabled = true;
        var h = engine.Rects[KeyIndex(model, "h")];
        engine.TouchDown(1, h.CenterX, h.CenterY, 0);
        engine.Frame(0);
        for (var i = 1; i <= 30; i++)
        {
            engine.TouchMotion(1, h.CenterX - (i * 10), h.CenterY, i);
        }

        Assert.True(engine.IsTracing);
        model.TraceAlpha = 1;
        model.Invalidate();
        var (r, g, b) = Pixel(window, h.CenterX - 150, h.CenterY + model.KeymapTop);
        Assert.InRange(r, 65, 85);
        Assert.InRange(g, 141, 161);
        Assert.InRange(b, 212, 232);
        window.Close();
    }

    private static int KeyIndex(KeyboardModel model, string name)
    {
        var grid = model.Engine.Grid;
        for (var cell = 0; cell < KeyGrid.DefaultRows * KeyGrid.DefaultColumns; cell++)
        {
            if (grid[cell] is { IsVisible: true, Plain: { } plain } && (plain.IsText ? plain.Text : plain.Name) == name)
            {
                return cell;
            }
        }

        throw new InvalidOperationException(name);
    }
}
