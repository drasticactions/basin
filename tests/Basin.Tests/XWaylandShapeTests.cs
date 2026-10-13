using Basin.Scene;
using Basin.XWayland;
using Xunit;

namespace Basin.Tests;

public sealed class XWaylandShapeTests
{
    [Fact]
    public void A_shaped_window_reports_its_shape_and_input_outside_it_falls_through()
    {
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes"), "Xwayland and xeyes are needed");
        using var host = new CompositorTestHost(320, 240);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        using var server = new XWaylandServer(host.Display, host.Loop);
        using var driver = new XWaylandSceneDriver
        {
            ShapeRenderer = host.Renderer,
            ManagedParent = _ => new SceneTree(host.Scene.Root),
        };
        var below = new SceneRect(host.Scene.Root, 320, 240, new RenderColor(0.2f, 0.6f, 0.3f, 1f));
        below.LowerToBottom();
        XWaylandWm? wm = null;
        XWaylandWindow? window = null;
        var shapeChanges = 0;
        server.Ready += fd =>
        {
            wm = new XWaylandWm(fd, host.Loop, shell, host.Seat);
            wm.WindowMapped += mapped =>
            {
                window ??= mapped;
                mapped.ShapeChanged += () => shapeChanges++;
            };
            driver.Attach(wm);
        };

        using var eyes = XWaylandTestSupport.Start("xeyes", server.DisplayName, "-geometry", "200x100+0+0");
        try
        {
            XWaylandTestSupport.Pump(host, () => window?.BoundingShape is not null && driver.SceneOf(window.Surface) is not null
                && window.Surface!.Current.Buffer is not null);
            var shape = window!.BoundingShape!;
            Assert.False(shape.IsEmpty);
            Assert.False(shape.Contains(1, 1));
            Assert.True(shape.Contains(50, 50));

            var scene = driver.SceneOf(window.Surface)!;
            foreach (var automatic in host.SurfaceScenes)
            {
                if (!ReferenceEquals(automatic, scene))
                {
                    automatic.Tree.Enabled = false;
                }
            }

            Assert.IsType<XWaylandShapeInputFilter>(scene.Content.InputFilter);
            var origin = window.X;
            Assert.Null(host.Scene.SurfaceAt(origin + 1, window.Y + 1));
            Assert.NotNull(host.Scene.SurfaceAt(origin + 50, window.Y + 50));
            Assert.True(host.Scene.NodeAt(origin + 1, window.Y + 1) is { Node: SceneRect });
        }
        finally
        {
            eyes.Kill();
            eyes.WaitForExit(2000);
            driver.Dispose();
            wm?.Dispose();
        }
    }
}
