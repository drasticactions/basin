using Pixman;

namespace Basin;

public interface IRenderer : IDisposable
{
    ITexture? ImportTexture(IBuffer buffer);

    IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options);

    DrmFormatSet DmabufTextureFormats => DrmFormatSet.Empty;

    IReadOnlyList<DrmFormat> ShmTextureFormats => ShmFormatLists.Basic;

    bool CanConvertYuv => false;

    bool RendersHalfFloat => false;

    bool ConvertToYuv(ITexture source, in FBox sourceBox, IBuffer target, YuvCoefficients coefficients, YuvRange range) => false;

    DrmFormatSet DmabufRenderFormats => DrmFormatSet.Empty;

    IRenderDevice? Device => null;

    ColorTransformCapability ColorTransform => ColorTransformCapability.None;

    RenderFencePrecision FencePrecision => RenderFencePrecision.None;

    int ExportLastSubmissionFence() => -1;

    bool SupportsFilter(TextureFilter filter) => filter != TextureFilter.Pixel;

    bool WaitsOnGpu => false;

    IColorLut? ImportLut(ColorLut3D lut) => null;

    IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms) => null;

    bool SupportsBackdropEffects => false;

    bool SupportsFrameFilters => false;
}
