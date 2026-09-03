using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class CtmStage : IPostStage
{
    private static readonly double[] IdentityMatrix = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IPixelShader? _shader;
    private readonly double[] _matrix = (double[])IdentityMatrix.Clone();
    private readonly PixelShaderUniformValue[] _uniforms = new PixelShaderUniformValue[9];
    private bool _identity = true;

    public CtmStage(IPixelShader? shader)
    {
        _shader = shader;
        Push();
    }

    public bool IsSupported => _shader is not null;

    public bool IsIdentity => _identity;

    public ReadOnlySpan<double> Matrix => _matrix;

    public void SetMatrix(ReadOnlySpan<double> rowMajor3x3)
    {
        if (rowMajor3x3.Length != 9)
        {
            throw new ArgumentException("a colour transform matrix has nine components", nameof(rowMajor3x3));
        }

        _thread.Assert();
        rowMajor3x3.CopyTo(_matrix);
        _identity = _matrix.AsSpan().SequenceEqual(IdentityMatrix);
        Push();
    }

    public void Reset() => SetMatrix(IdentityMatrix);

    public void Render(IRenderPass pass, ITexture frame, in PostContext context)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(frame);
        _thread.Assert();
        pass.AddTexture(frame, new TextureRenderOptions
        {
            DstBox = new Box(0, 0, context.Width, context.Height),
            Shader = _identity ? null : _shader,
        });
    }

    private void Push()
    {
        if (_shader is null)
        {
            return;
        }

        for (var i = 0; i < 9; i++)
        {
            _uniforms[i] = (float)_matrix[i];
        }

        _shader.SetUniforms(_uniforms);
    }
}
