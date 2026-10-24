using Pixman;

namespace Basin;

/// <summary>
/// Represents one renderer behind the render contract: import buffers as textures, begin a pass on a target buffer, and draw rects and textures into it. Every other ability is declared, not assumed.
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>
    /// Makes the content of a buffer drawable. The texture follows the buffer, so a pass after a content change reads the new pixels. It never throws for a foreign or hostile buffer.
    /// </summary>
    /// <param name="buffer">The buffer to import.</param>
    /// <returns>The texture, which the caller disposes, or <see langword="null"/> when the renderer cannot import the buffer.</returns>
    ITexture? ImportTexture(IBuffer buffer);

    /// <summary>
    /// Begins a pass that draws into a target buffer. The wait fence in <paramref name="options"/> is borrowed for this call only.
    /// </summary>
    /// <param name="target">The buffer to draw into.</param>
    /// <param name="options">The fences and the output color description of the pass.</param>
    /// <returns>The pass. The caller must call <see cref="IRenderPass.Submit"/> before the next pass begins.</returns>
    /// <exception cref="RendererLostException">The renderer lost its device. After a loss, every other member can also throw or return <see langword="null"/>.</exception>
    IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options);

    /// <summary>
    /// Gets the dmabuf formats and modifiers that <see cref="ImportTexture"/> can sample. The default is an empty set.
    /// </summary>
    DrmFormatSet DmabufTextureFormats => DrmFormatSet.Empty;

    /// <summary>
    /// Gets the shared-memory formats that <see cref="ImportTexture"/> can sample. The default is the basic ARGB and XRGB list.
    /// </summary>
    IReadOnlyList<DrmFormat> ShmTextureFormats => ShmFormatLists.Basic;

    /// <summary>
    /// Gets a value indicating whether <see cref="ConvertToYuv"/> can write anything. The default is <see langword="false"/>.
    /// </summary>
    bool CanConvertYuv => false;

    /// <summary>
    /// Gets a value indicating whether a memory buffer in a 16-bit float format can be a render target. The default is <see langword="false"/>.
    /// </summary>
    bool RendersHalfFloat => false;

    /// <summary>
    /// Converts a texture into a linear two-plane NV12 dmabuf, outside a pass. A source with another aspect ratio is centered with black bars.
    /// </summary>
    /// <param name="source">The texture to read.</param>
    /// <param name="sourceBox">The region of the texture to read, in buffer pixels.</param>
    /// <param name="target">The NV12 buffer to write.</param>
    /// <param name="coefficients">The YUV matrix to use.</param>
    /// <param name="range">The quantization range to write.</param>
    /// <returns><see langword="false"/> when nothing was written, which is the default.</returns>
    bool ConvertToYuv(ITexture source, in FBox sourceBox, IBuffer target, YuvCoefficients coefficients, YuvRange range) => false;

    /// <summary>
    /// Gets the dmabuf formats and modifiers that a pass can draw into. The default is an empty set.
    /// </summary>
    DrmFormatSet DmabufRenderFormats => DrmFormatSet.Empty;

    /// <summary>
    /// Gets the DRM device that the renderer draws on, or <see langword="null"/> for a CPU renderer, which is the default.
    /// </summary>
    IRenderDevice? Device => null;

    /// <summary>
    /// Gets how the renderer takes a color conversion. The default is <see cref="ColorTransformCapability.None"/>.
    /// </summary>
    ColorTransformCapability ColorTransform => ColorTransformCapability.None;

    /// <summary>
    /// Gets the color space in which the renderer blends translucent content. The default is <see cref="RenderBlendSpace.Encoded"/>.
    /// </summary>
    RenderBlendSpace BlendSpace => RenderBlendSpace.Encoded;

    /// <summary>
    /// Gets how much a signal fence of a pass promises. The default is <see cref="RenderFencePrecision.None"/>.
    /// </summary>
    RenderFencePrecision FencePrecision => RenderFencePrecision.None;

    /// <summary>
    /// Exports a fence for the most recently submitted pass.
    /// </summary>
    /// <returns>A sync_file fd that the caller owns and that signals when the pass finished, or -1 when the renderer cannot make one, which is the default.</returns>
    int ExportLastSubmissionFence() => -1;

    /// <summary>
    /// Tells whether the renderer draws a texture filter as specified. A renderer that does not draws <see cref="TextureFilter.Pixel"/> as <see cref="TextureFilter.Nearest"/>.
    /// </summary>
    /// <param name="filter">The filter to check.</param>
    /// <returns><see langword="true"/> when the renderer draws the filter itself. The default is <see langword="true"/> for every filter except <see cref="TextureFilter.Pixel"/>.</returns>
    bool SupportsFilter(TextureFilter filter) => filter != TextureFilter.Pixel;

    /// <summary>
    /// Gets a value indicating whether the GPU waits for the wait fence of a pass. When it is <see langword="false"/>, which is the default, the calling thread blocks on the fence.
    /// </summary>
    bool WaitsOnGpu => false;

    /// <summary>
    /// Imports a color lookup table for <see cref="TextureRenderOptions.Lut"/>. Import a table once, not each frame.
    /// </summary>
    /// <param name="lut">The table to import.</param>
    /// <returns>The imported table, which the caller disposes, or <see langword="null"/> when the renderer does not take tables, which is the default. It is never <see langword="null"/> on a renderer that declares <see cref="ColorTransformCapability.Lut3D"/>.</returns>
    IColorLut? ImportLut(ColorLut3D lut) => null;

    /// <summary>
    /// Compiles a consumer pixel shader from the dialect that the renderer reads. A source that does not compile throws with the compiler log.
    /// </summary>
    /// <param name="source">The shader source in one or more dialects.</param>
    /// <param name="uniforms">The uniforms that the shader declares after the uniforms of basin, in order.</param>
    /// <returns>The shader, which the caller disposes, or <see langword="null"/> when the source has no dialect for this renderer. The default always returns <see langword="null"/>.</returns>
    IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms) => null;

    /// <summary>
    /// Gets a value indicating whether <see cref="IRenderPass.AddBackdropEffect(IBackdropEffect, in Box, PixmanRegion32?, object?)"/> does anything. The default is <see langword="false"/>.
    /// </summary>
    bool SupportsBackdropEffects => false;

    /// <summary>
    /// Gets a value indicating whether <see cref="IRenderPass.AddFrameFilter"/> can run a filter. The default is <see langword="false"/>.
    /// </summary>
    bool SupportsFrameFilters => false;

    /// <summary>
    /// Occurs at most once, when the renderer finds that its GPU device or context is lost. A handler must only queue work, because the detecting call is still on the stack. The default implementation never raises it.
    /// </summary>
    event Action? DeviceLost
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <summary>
    /// Gets how the renderer finds a loss of its device. The default is <see cref="RendererLossDetection.Never"/>.
    /// </summary>
    RendererLossDetection LossDetection => RendererLossDetection.Never;

    /// <summary>
    /// Makes the GPU context of the renderer current on the calling thread, so that objects of this renderer can be released. The default does nothing.
    /// </summary>
    void MakeCurrent()
    {
    }
}
