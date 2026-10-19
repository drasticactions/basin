using Basin.Desktop.Protocol;
using Basin.Sdl;
using SDL3;
using Wayland;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlCursorTests
{
    [Fact]
    public void A_client_surface_cursor_becomes_the_SDL_cursor()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var pointer = harness.Client.Seat!.GetPointer();
        uint serial = 0;
        pointer.Enter += (_, e) => serial = e.Serial;
        harness.MapToplevel(200, 150, (data, stride) => { });
        var box = harness.Comp.Shell.Windows[0].ClientBox;
        SdlEvents.Motion(harness.Comp.Window.Window.Id, box.X + 40, box.Y + 40);
        harness.PumpUntil(() => serial != 0, "the pointer never entered the client");

        var cursorSurface = harness.Client.Compositor.CreateSurface();
        var buffer = harness.Client.CreateBuffer(16, 16, (data, stride) =>
        {
            unsafe
            {
                for (var y = 0; y < 16; y++)
                {
                    var row = (uint*)((byte*)data + (y * stride));
                    for (var x = 0; x < 16; x++)
                    {
                        row[x] = 0xFFFF0000u;
                    }
                }
            }
        }, WlShm.Format.Argb8888);
        cursorSurface.Attach(buffer.Proxy, 0, 0);
        cursorSurface.Damage(0, 0, 16, 16);
        cursorSurface.Commit();
        pointer.SetCursor(serial, cursorSurface, 3, 4);
        var cursor = harness.Comp.Cursor;
        harness.PumpUntil(() => cursor.Current != 0 && cursor.CurrentSystem is null, "the surface cursor never reached SDL");
        Assert.Equal(cursor.Current, SDL.GetCursor());

        pointer.SetCursor(serial, null, 0, 0);
        harness.PumpUntil(() => cursor.IsHidden, "a null cursor never hid the SDL cursor");
    }

    [Fact]
    public void A_cursor_shape_maps_to_an_SDL_system_cursor()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var pointer = harness.Client.Seat!.GetPointer();
        uint serial = 0;
        pointer.Enter += (_, e) => serial = e.Serial;
        var manager = harness.Client.BindAt<WpCursorShapeManagerV1>("wp_cursor_shape_manager_v1", 1);
        var device = manager.GetPointer(pointer);
        harness.MapToplevel(200, 150, (data, stride) => { });
        var box = harness.Comp.Shell.Windows[0].ClientBox;
        SdlEvents.Motion(harness.Comp.Window.Window.Id, box.X + 40, box.Y + 40);
        harness.PumpUntil(() => serial != 0, "the pointer never entered the client");

        device.SetShape(serial, WpCursorShapeDeviceV1.Shape.Text);
        var cursor = harness.Comp.Cursor;
        harness.PumpUntil(() => cursor.CurrentSystem == SDL.SystemCursor.Text, "the shape never reached SDL");

        device.SetShape(serial, WpCursorShapeDeviceV1.Shape.NwseResize);
        harness.PumpUntil(() => cursor.CurrentSystem == SDL.SystemCursor.NWSEResize, "the second shape never reached SDL");
    }

    [Fact]
    public void Every_mapped_cursor_is_one_SDL_3_4_has()
    {
        foreach (var shape in Enum.GetValues<Basin.Capabilities.CursorShape>())
        {
            Assert.InRange((int)SdlCursor.Map(shape), 0, (int)SDL.SystemCursor.WResize);
        }

        Assert.Equal(SDL.SystemCursor.SEResize, SdlCursor.Map("bottom_right_corner"));
        Assert.Equal(SDL.SystemCursor.Move, SdlCursor.Map("fleur"));
        Assert.Equal(SDL.SystemCursor.Default, SdlCursor.Map("left_ptr"));
    }
}
