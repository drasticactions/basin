using Pixman;

namespace Basin;

public interface IPixelShader : IDisposable
{
    void SetUniforms(ReadOnlySpan<PixelShaderUniformValue> values);
}
