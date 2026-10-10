using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Frames.MsStyles;
using Basin.Frames.MsStyles.Quill;
using Basin.Frames.Quill;
using Basin.UI.Quill;
using Xunit;

namespace Basin.Tests;

public sealed class MsStylesQuillFrameTests
{
    private static readonly (string Theme, MsStylesLook Look, string State, double Scale, string Golden)[] Cases =
    [
        ("Plain", MsStylesLook.ClassicXp, "active", 1.0, "msstyles-quill-plain-active"),
        ("Plain", MsStylesLook.ClassicXp, "inactive", 2.0, "msstyles-quill-plain-inactive-2x"),
        ("Plain", MsStylesLook.ClassicXp, "maximized", 1.0, "msstyles-quill-plain-maximized"),
        ("Plain", MsStylesLook.ClassicXp, "no-minimize", 1.0, "msstyles-quill-plain-no-minimize"),
        ("Glassy", MsStylesLook.Basic, "active", 1.0, "msstyles-quill-glassy-basic-active"),
        ("Glassy", MsStylesLook.Basic, "inactive", 1.5, "msstyles-quill-glassy-basic-inactive-1.5x"),
        ("Glassy", MsStylesLook.Glass, "active", 1.0, "msstyles-quill-glassy-glass-active"),
        ("Glassy", MsStylesLook.Glass, "inactive", 2.0, "msstyles-quill-glassy-glass-inactive-2x"),
        ("Glassy", MsStylesLook.Glass, "maximized", 1.0, "msstyles-quill-glassy-glass-maximized"),
        ("Glassy", MsStylesLook.Glass, "dialog", 1.0, "msstyles-quill-glassy-glass-dialog"),
    ];

    private static readonly (string Row, string Backend)[] Rows =
    [
        ("gl", "gl"),
        ("skia-gl", "gl"),
        ("vulkan", "vulkan"),
        ("skia-vulkan", "vulkan"),
        ("skia-graphite", "vulkan"),
    ];

    public static TheoryData<string, string, int> Goldens
    {
        get
        {
            var data = new TheoryData<string, string, int>();
            foreach (var (row, backend) in Rows)
            {
                for (var i = 0; i < Cases.Length; i++)
                {
                    data.Add(row, backend, i);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Goldens))]
    public void Quill_frame_matches_golden(string row, string backend, int index)
    {
        var (theme, look, state, scale, golden) = Cases[index];
        CompositorTestHost.SkipUnlessRunnable(row);
        Assert.SkipWhen(!CompositorTestHost.GoldensComparable(row), $"{row} goldens are not comparable on this driver");
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"this row builds no {backend} quill host");
        using var rig = new MsStylesRig(theme, look);
        using var text = new MsStylesQuillText(rig.Fonts) { Fallback = QuillFrameFonts.Bundled() };
        var renderer = new MsStylesQuillFrameRenderer(rig.Style, new MsStylesQuillPainter(rig.Style.Images, text));
        var frameState = MsStylesRig.StateFor(state);
        var insets = renderer.Measure(frameState, scale);
        var outsets = renderer.Outsets(frameState, scale);
        const int clientWidth = 200;
        const int clientHeight = 40;
        var surface = (IQuillUISurface)lease!.Host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = clientWidth + insets.Left + insets.Right + outsets.Left + outsets.Right,
            Height = clientHeight + insets.Top + insets.Bottom + outsets.Top + outsets.Bottom,
            Scale = scale,
        })!;

        renderer.Draw(surface, new Box(insets.Left + outsets.Left, insets.Top + outsets.Top, clientWidth, clientHeight), frameState, default);
        Assert.True(surface.TryAcquire(out var frame));
        try
        {
            var buffer = frame.Buffer!;
            Assert.True(BufferCapture.TryReadRgba(buffer, host.Renderer, out var rgba));
            Golden.AssertMatches(rgba!, buffer.Width, buffer.Height, $"{golden}-{(backend == "vulkan" ? "vk" : "gl")}{(row == "vulkan" ? "-linear" : string.Empty)}", tolerance: 6);
        }
        finally
        {
            frame.Dispose();
            surface.Dispose();
        }
    }

    [Fact]
    public void The_quill_renderer_hits_what_the_skia_renderer_hits()
    {
        using var rig = new MsStylesRig("Glassy", MsStylesLook.Glass);
        using var text = new MsStylesQuillText(rig.Fonts) { Fallback = QuillFrameFonts.Bundled() };
        var quill = new MsStylesQuillFrameRenderer(rig.Style, new MsStylesQuillPainter(rig.Style.Images, text));
        var skia = rig.Renderer();
        var state = MsStylesRig.StateFor("active");

        Assert.Equal(typeof(IQuillUISurface), quill.SurfaceContract);
        Assert.Equal(skia.Measure(state, 1.0), quill.Measure(state, 1.0));
        Assert.Equal(skia.Outsets(state, 1.0), quill.Outsets(state, 1.0));
        Assert.Throws<ArgumentException>(() => new MsStylesQuillFrameRenderer(
            rig.Style,
            new MsStylesQuillPainter(new MsStylesImageCache(), text)));
    }
}
