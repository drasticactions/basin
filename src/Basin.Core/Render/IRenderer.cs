using Pixman;

namespace Basin;

public interface IRenderer : IDisposable
{
    ITexture? ImportTexture(IBuffer buffer);

    IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options);

    DrmFormatSet DmabufTextureFormats => DrmFormatSet.Empty;

    IRenderDevice? Device => null;

    ColorTransformCapability ColorTransform => ColorTransformCapability.None;

    RenderFencePrecision FencePrecision => RenderFencePrecision.None;

    int ExportLastSubmissionFence() => -1;

    bool WaitsOnGpu => false;

    IColorLut? ImportLut(ColorLut3D lut) => null;

    IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms) => null;

    bool SupportsBackdropEffects => false;

    bool SupportsFrameFilters => false;
}
