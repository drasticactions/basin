using Basin.Capabilities;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class FrameUnderlayTests
{
    [Fact]
    public void The_underlay_sits_under_the_strips_at_the_outer_box()
    {
        using var testHost = new CompositorTestHost();
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var renderer = new ShapedFrameRenderer();
        var effect = new ProbeShapedEffect();
        var frame = new Frame(host, renderer, scene.Root) { UnderlayEffect = effect };

        frame.Configure(new Box(40, 60, 120, 90), 1.0, new FrameState { Title = "glass", Active = true });
        frame.Commit();

        var underlay = frame.UnderlayNode;
        Assert.Same(underlay, frame.Tree.Children[0]);
        Assert.True(underlay.Enabled);
        Assert.True(underlay.HasActiveBackdrop);
        Assert.Same(effect, underlay.BackdropEffect);
        Assert.Same(underlay, underlay.BackdropKey);
        Assert.Equal((0, 0), (underlay.X, underlay.Y));
        Assert.Equal((128, 128), underlay.DisplaySize);
        var region = underlay.BackdropRegion!;
        Assert.False(Covers(region, 0, 0), "the corner is outside the rounded region");
        Assert.True(Covers(region, 64, 80), "the client area is inside it");
        Assert.Equal(1, effect.Shapes);
        Assert.Equal(renderer.Shape, effect.Last);
        Assert.Same(underlay, effect.LastKey);
        Assert.False(underlay.AcceptsInputAt(64, 2));

        frame.Dispose();
        Assert.Equal(1, effect.Forgotten);
        scene.Root.Destroy();
    }

    [Fact]
    public void A_frame_without_a_shape_or_an_effect_has_no_underlay()
    {
        using var testHost = new CompositorTestHost();
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var shaped = new Frame(host, new ShapedFrameRenderer(), scene.Root);
        var plain = new Frame(host, new ShapedFrameRenderer { Publishes = false }, scene.Root) { UnderlayEffect = new ProbeShapedEffect() };
        foreach (var frame in new[] { shaped, plain })
        {
            frame.Configure(new Box(40, 60, 120, 90), 1.0, new FrameState { Title = "x" });
            frame.Commit();
            Assert.False(frame.UnderlayNode.Enabled);
            Assert.False(frame.UnderlayNode.HasActiveBackdrop);
            frame.Dispose();
        }

        scene.Root.Destroy();
    }

    [Fact]
    public void Without_client_glass_the_hole_is_cut_out()
    {
        using var testHost = new CompositorTestHost();
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var frame = new Frame(host, new ShapedFrameRenderer(), scene.Root)
        {
            UnderlayEffect = new ProbeShapedEffect(),
            UnderlayCoversClient = false,
        };

        frame.Configure(new Box(40, 60, 120, 90), 1.0, new FrameState { Title = "x" });
        frame.Commit();
        var region = frame.UnderlayNode.BackdropRegion!;
        Assert.False(Covers(region, 64, 80));
        Assert.True(Covers(region, 64, 10));
        Assert.True(Covers(region, 1, 80));
        frame.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void A_changed_shape_is_forwarded_and_damages_the_underlay()
    {
        using var testHost = new CompositorTestHost();
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var renderer = new ShapedFrameRenderer();
        var effect = new ProbeShapedEffect();
        var clock = new Basin.Capabilities.Defaults.FrameClock();
        var now = 1_000_000_000L;
        var frame = new Frame(host, renderer, scene.Root) { UnderlayEffect = effect, UnderlayKey = "key", Clock = clock, Time = () => now };
        frame.Configure(new Box(40, 60, 120, 90), 1.0, new FrameState { Title = "x", Active = true });
        frame.Commit();
        Assert.Equal("key", frame.UnderlayNode.BackdropKey);

        var published = new List<FrameBackdropShape>();
        frame.ShapePublished += published.Add;
        var damaged = 0;
        scene.Damaged += (_, _) => damaged++;
        frame.PointerMotion(80, 40);
        Assert.True(frame.Animating);
        clock.BeginFrame(testHost.Output, now + 16_000_000);
        Assert.Equal(2, effect.Shapes);
        Assert.Equal(44, effect.Last.HighlightX);
        Assert.Equal("key", effect.LastKey);
        Assert.True(damaged > 0);
        Assert.Equal(44, Assert.Single(published).HighlightX);

        var settled = effect.Shapes;
        now += 500_000_000;
        clock.BeginFrame(testHost.Output, now);
        clock.BeginFrame(testHost.Output, now + 16_000_000);
        Assert.Equal(settled, effect.Shapes);
        frame.Dispose();
        Assert.Equal(1, effect.Forgotten);
        scene.Root.Destroy();
    }

    private static bool Covers(Pixman.PixmanRegion32 region, int x, int y)
    {
        foreach (var rect in RegionRects.Of(region))
        {
            if (x >= rect.X1 && x < rect.X2 && y >= rect.Y1 && y < rect.Y2)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ProbeShapedEffect : IShapedBackdropEffect
    {
        public int Shapes { get; private set; }

        public FrameBackdropShape Last { get; private set; }

        public object? LastKey { get; private set; }

        public int Forgotten { get; private set; }

        public void SetShape(object key, in FrameBackdropShape shape)
        {
            Shapes++;
            Last = shape;
            LastKey = key;
        }

        public bool ForgetSurface(object key)
        {
            Forgotten++;
            return true;
        }
    }

    private sealed class ShapedFrameRenderer : IFrameRenderer
    {
        private const int Border = 4;
        private const int TitleHeight = 30;
        private int _width;
        private int _height;
        private double _glint;

        public bool Publishes { get; init; } = true;

        public FrameBackdropShape Shape => new()
        {
            Width = _width,
            Height = _height,
            CornerRadius = 8,
            EdgeWidth = 6,
            ClientHole = new Box(Border, Border + TitleHeight, _width - (2 * Border), _height - TitleHeight - (2 * Border)),
            HighlightX = _glint,
            Emphasis = 1,
        };

        public FrameInsets Measure(in FrameState state, double scale) => new(Border + TitleHeight, Border, Border, Border);

        public bool Animating(in FrameState state, in FrameInteraction interaction) =>
            interaction.PointerInside && _glint != interaction.PointerX;

        public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
        {
            _width = clientBox.Width + (2 * Border);
            _height = clientBox.Height + TitleHeight + (2 * Border);
            if (interaction.PointerInside && interaction.Now > interaction.PointerChangedAt)
            {
                _glint = interaction.PointerX;
            }

            var falsifier = (FalsifierUISurface)surface;
            _ = falsifier.BeginPixels();
            falsifier.EndPixels();
        }

        public bool BackdropShape(in FrameState state, double scale, out FrameBackdropShape shape)
        {
            shape = Shape;
            return Publishes && _width > 0;
        }

        public FramePart PartAt(double x, double y, in FrameState state, double scale) =>
            x < 0 || y < 0 || x >= _width || y >= _height ? FramePart.None
            : y < Border + TitleHeight ? FramePart.Title
            : FramePart.Border;
    }
}
