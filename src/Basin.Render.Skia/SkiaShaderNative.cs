using SkiaSharp;

namespace Basin.Render.Skia;

internal static class SkiaShaderNative
{
    internal struct Uniform
    {
        public nint Name;
        public nint NameLength;
        public nint Offset;
        public int Type;
        public int Count;
        public int Flags;
    }

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_runtimeeffect_get_uniform_from_index")]
    public static extern unsafe void GetUniform(nint effect, int index, Uniform* uniform);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_runtimeeffect_make_shader")]
    public static extern unsafe nint MakeShader(nint effect, nint uniforms, nint* children, nint childCount, SKMatrix* localMatrix);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_data_new_with_copy")]
    public static extern unsafe nint CopyData(void* source, nint length);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_data_unref")]
    public static extern void UnrefData(nint data);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_image_make_shader")]
    public static extern unsafe nint ImageShader(nint image, SKShaderTileMode tileX, SKShaderTileMode tileY, SKSamplingOptions* sampling, SKMatrix* matrix);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_shader_with_color_filter")]
    public static extern nint WithColorFilter(nint shader, nint filter);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_shader_unref")]
    public static extern void UnrefShader(nint shader);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_paint_set_shader")]
    public static extern void SetPaintShader(nint paint, nint shader);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_paint_set_colorfilter")]
    public static extern void SetPaintColorFilter(nint paint, nint filter);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_colorfilter_new_color_matrix")]
    public static extern unsafe nint ColorMatrix(float* matrix);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_colorfilter_new_compose")]
    public static extern nint ComposeColorFilters(nint outer, nint inner);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_colorfilter_unref")]
    public static extern void UnrefColorFilter(nint filter);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_rrect_new")]
    public static extern nint NewRoundRect();

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_rrect_delete")]
    public static extern void DeleteRoundRect(nint rrect);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_rrect_set_rect_xy")]
    public static extern unsafe void SetRoundRect(nint rrect, SKRect* rect, float radiusX, float radiusY);

    [System.Runtime.InteropServices.DllImport("libSkiaSharp", EntryPoint = "sk_canvas_clip_rrect_with_operation")]
    public static extern void ClipRoundRect(nint canvas, nint rrect, SKClipOperation operation, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.U1)] bool antialias);
}
