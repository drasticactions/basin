using Basin.Capabilities;
using Xunit;

namespace Basin.Testing;

public abstract class ScreenCaptureConformance
{
    protected abstract IScreenCapture Create();

    protected abstract IOutput Output { get; }

    protected abstract IBuffer CreateTarget(in CaptureFormat format);

    protected abstract void DestroyTarget(IBuffer target);

    [Fact]
    public void Describes_every_source_it_supports()
    {
        var capture = Create();
        var source = CaptureSource.Output(Output);
        if (!capture.Supports(source))
        {
            return;
        }

        Assert.True(capture.TryDescribe(source, out var format));
        Assert.True(format.Width > 0 && format.Height > 0);
        Assert.Equal(format.Width * format.Format.BytesPerPixel(), format.Stride);
    }

    [Fact]
    public void An_unsupported_source_is_never_described_or_captured()
    {
        var capture = Create();
        var source = CaptureSource.Toplevel(0);
        if (capture.Supports(source))
        {
            return;
        }

        Assert.False(capture.TryDescribe(source, out _));
    }

    [Fact]
    public void Capture_fills_the_target_for_an_empty_region()
    {
        var capture = Create();
        var source = CaptureSource.Output(Output);
        Assert.True(capture.Supports(source));
        Assert.True(capture.TryDescribe(source, out var format));

        var target = CreateTarget(format);
        try
        {
            Assert.True(capture.Capture(source, default, target));
        }
        finally
        {
            DestroyTarget(target);
        }
    }

    [Fact]
    public void Capture_respects_a_partial_region()
    {
        var capture = Create();
        var source = CaptureSource.Output(Output);
        Assert.True(capture.TryDescribe(source, out var format));
        if (format.Width < 4 || format.Height < 4)
        {
            return;
        }

        var region = new Box(1, 1, format.Width / 2, format.Height / 2);
        var target = CreateTarget(new CaptureFormat(region.Width, region.Height, format.Format));
        try
        {
            Assert.True(capture.Capture(source, region, target));
        }
        finally
        {
            DestroyTarget(target);
        }
    }

    [Fact]
    public void A_cursor_state_is_answered_consistently()
    {
        var capture = Create();

        if (capture.TryCursorState(Output, out var cursor))
        {
            Assert.True(cursor.IsVisible);
            Assert.True(cursor.Width > 0 && cursor.Height > 0);
        }
    }

    [Fact]
    public void A_published_cursor_is_reported_and_withdrawn()
    {
        var capture = Create();
        if (!capture.Supports(CaptureSource.Cursor(Output)))
        {
            return;
        }

        var image = CreateTarget(new CaptureFormat(64, 64, DrmFormat.Argb8888));
        try
        {
            capture.SetCursor(image, new CaptureCursorState(10, 12, 4, 5, 24, 24, IsVisible: true));
            Assert.True(capture.TryDescribe(CaptureSource.Cursor(Output), out var format));
            Assert.Equal(24, format.Width);
            Assert.Equal(24, format.Height);

            Assert.True(capture.TryCursorState(Output, out var cursor));
            Assert.True(cursor.IsVisible);
            Assert.Equal(24, cursor.Width);
            Assert.Equal(24, cursor.Height);
            Assert.Equal(4, cursor.HotspotX);
            Assert.Equal(5, cursor.HotspotY);

            capture.SetCursor(null, default);
            Assert.False(capture.TryCursorState(Output, out _));
            Assert.False(capture.TryDescribe(CaptureSource.Cursor(Output), out _));
        }
        finally
        {
            DestroyTarget(image);
        }
    }

    [Fact]
    public void Capture_allocates_nothing_across_1000_frames()
    {
        var capture = Create();
        var source = CaptureSource.Output(Output);
        Assert.True(capture.TryDescribe(source, out var format));

        var target = CreateTarget(format);
        try
        {
            for (var i = 0; i < 20; i++)
            {
                Drive(capture, source, target);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                Drive(capture, source, target);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        finally
        {
            DestroyTarget(target);
        }
    }

    private static void Drive(IScreenCapture capture, in CaptureSource source, IBuffer target)
    {
        _ = capture.Supports(source);
        _ = capture.TryDescribe(source, out _);
        _ = capture.Capture(source, default, target);
        _ = capture.TryCursorState(source.OutputTarget!, out _);
    }
}
