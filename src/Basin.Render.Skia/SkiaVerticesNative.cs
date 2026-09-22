using SkiaSharp;

namespace Basin.Render.Skia;

internal static class SkiaVerticesNative
{
    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_vertices_make_copy")]
    public static extern unsafe nint MakeCopy(
        int vertexMode, int vertexCount, SKPoint* positions, SKPoint* texs, uint* colors, int indexCount, ushort* indices);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_vertices_unref")]
    public static extern void Unref(nint vertices);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_canvas_draw_vertices")]
    public static extern void Draw(nint canvas, nint vertices, int blendMode, nint paint);
}
