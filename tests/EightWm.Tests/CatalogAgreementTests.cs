using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using AvaWin.Animations;
using Xunit;

namespace EightWm.Tests;

public sealed class CatalogAgreementTests
{
    private enum Channel
    {
        OffsetX,
        OffsetY,
        Scale,
        Opacity,
    }

    private static double Read(Border element, Channel channel)
    {
        var matrix = element.RenderTransform is TransformOperations operations
            ? operations.Value
            : Avalonia.Matrix.Identity;
        return channel switch
        {
            Channel.OffsetX => matrix.M31,
            Channel.OffsetY => matrix.M32,
            Channel.Scale => matrix.M11,
            _ => element.Opacity,
        };
    }

    private static double Expected(in AnimationSpec spec, in Track track, Channel channel, double sign, int index, double timeMs)
    {
        var local = timeMs - spec.DelayFor(index) - track.DelayMs;
        var progress = Math.Clamp(local / track.DurationMs, 0, 1);
        var value = track.From + ((track.To - track.From) * Curves.Evaluate(track.Curve, progress));
        return channel is Channel.OffsetX or Channel.OffsetY ? value * sign : value;
    }

    private static async Task Agree(
        Animation name,
        Func<AnimationSpec, Track> pick,
        Channel channel,
        Func<IReadOnlyList<Control>, Task> run,
        double sign = 1,
        int elements = 1)
    {
        var spec = AnimationCatalog.Of(name);
        var track = pick(spec);
        Assert.False(track.IsEmpty, $"{name} carries no track for {channel}");

        var panel = new StackPanel();
        var targets = new List<Border>();
        for (var i = 0; i < elements; i++)
        {
            var border = new Border { Width = 40, Height = 40 };
            targets.Add(border);
            panel.Children.Add(border);
        }

        var window = new Window { Content = panel, Width = 400, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var scale = WinAnimations.TimeScale;
        WinAnimations.TimeScale = 1;
        try
        {
            var task = run(targets);
            var final = channel is Channel.OffsetX or Channel.OffsetY ? track.To * sign : track.To;
            for (var i = 0; i < 50 && !task.IsCompleted && Math.Abs(Read(targets[0], channel) - final) < 1e-9; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(1);
            }

            var clock = Stopwatch.StartNew();
            var range = Math.Max(Math.Abs(track.To - track.From), 1e-6);
            var end = 0.0;
            for (var i = 0; i < elements; i++)
            {
                end = Math.Max(end, spec.DelayFor(i) + spec.DurationMs);
            }

            var checkedSamples = 0;
            while (!task.IsCompleted && clock.ElapsedMilliseconds < end + 1000)
            {
                await Task.Delay(4);
                var before = clock.Elapsed.TotalMilliseconds;
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var after = clock.Elapsed.TotalMilliseconds;
                if (task.IsCompleted)
                {
                    break;
                }

                for (var index = 0; index < elements; index++)
                {
                    var actual = Read(targets[index], channel);
                    var low = Expected(spec, track, channel, sign, index, before - 40);
                    var high = Expected(spec, track, channel, sign, index, after + 15);
                    var slack = range * 0.02;
                    Assert.True(
                        actual >= Math.Min(low, high) - slack && actual <= Math.Max(low, high) + slack,
                        $"{name} {channel} element {index} at {before:F0}..{after:F0}ms: EightWm's catalog gives {low:F3}..{high:F3}, AvaWin's WinAnimations gives {actual:F3}. Fix whichever disagrees with WinJS's Animations.js.");
                    checkedSamples++;
                }
            }

            var finished = clock.Elapsed.TotalMilliseconds;
            Assert.True(task.IsCompleted, $"{name}: AvaWin ran past {finished:F0}ms against EightWm's {end}ms");
            Assert.InRange(finished, end - 5, end + 40);
            Assert.True(checkedSamples > 10, $"{name}: only {checkedSamples} samples");
        }
        finally
        {
            WinAnimations.TimeScale = scale;
            window.Close();
        }
    }

    [AvaloniaFact]
    public Task Show_panel_agrees() =>
        Agree(Animation.ShowPanel, spec => spec.Offset, Channel.OffsetX, list => WinAnimations.ShowPanel(list));

    [AvaloniaFact]
    public Task Hide_panel_agrees() =>
        Agree(Animation.HidePanel, spec => spec.Offset, Channel.OffsetX, list => WinAnimations.HidePanel(list));

    [AvaloniaFact]
    public Task Show_edge_ui_agrees() =>
        Agree(Animation.ShowEdgeUi, spec => spec.Offset, Channel.OffsetY, list => WinAnimations.ShowEdgeUI(list), sign: -1);

    [AvaloniaFact]
    public Task Hide_edge_ui_agrees() =>
        Agree(Animation.HideEdgeUi, spec => spec.Offset, Channel.OffsetY, list => WinAnimations.HideEdgeUI(list), sign: -1);

    [AvaloniaFact]
    public Task Enter_page_offset_and_stagger_agree() =>
        Agree(Animation.EnterPage, spec => spec.Offset, Channel.OffsetX, list => WinAnimations.EnterPage(list), elements: 6);

    [AvaloniaFact]
    public Task Enter_page_fade_agrees() =>
        Agree(Animation.EnterPage, spec => spec.Opacity, Channel.Opacity, list => WinAnimations.EnterPage(list), elements: 6);

    [AvaloniaFact]
    public Task Fade_in_agrees() =>
        Agree(Animation.FadeIn, spec => spec.Opacity, Channel.Opacity, list => WinAnimations.FadeIn(list));

    [AvaloniaFact]
    public Task Cross_fade_out_agrees() =>
        Agree(Animation.CrossFadeOut, spec => spec.Opacity, Channel.Opacity, list => WinAnimations.CrossFade([], list));

    [AvaloniaFact]
    public Task Drag_source_start_scale_agrees() =>
        Agree(Animation.DragSourceStart, spec => spec.Scale, Channel.Scale, list => WinAnimations.DragSourceStart(list));

    [AvaloniaFact]
    public Task Drag_source_start_fade_agrees() =>
        Agree(Animation.DragSourceStart, spec => spec.Opacity, Channel.Opacity, list => WinAnimations.DragSourceStart(list));

    [AvaloniaFact]
    public Task Drag_source_end_scale_agrees() =>
        Agree(Animation.DragSourceEnd, spec => spec.Scale, Channel.Scale, list => WinAnimations.DragSourceEnd(list));

    [AvaloniaFact]
    public Task Drag_source_end_fade_agrees() =>
        Agree(Animation.DragSourceEnd, spec => spec.Opacity, Channel.Opacity, list => WinAnimations.DragSourceEnd(list));

    [AvaloniaFact]
    public void The_standard_curve_is_the_same_bezier()
    {
        foreach (var t in new[] { 0.1, 0.25, 0.5, 0.75, 0.9 })
        {
            Assert.True(
                Math.Abs(WinEasing.Standard.Ease(t) - Curves.Evaluate(AnimationCurve.Deceleration, t)) < 0.002,
                $"at {t}: AvaWin {WinEasing.Standard.Ease(t):F4}, EightWm {Curves.Evaluate(AnimationCurve.Deceleration, t):F4}");
        }
    }
}
