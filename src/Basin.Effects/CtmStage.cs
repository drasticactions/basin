using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// A post stage that applies a color transform matrix in linear light. An identity matrix or a missing shader draws the frame unchanged.
/// </summary>
public sealed class CtmStage : IPostStage
{
    private static readonly double[] IdentityMatrix = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IPixelShader? _shader;
    private readonly double[] _matrix = (double[])IdentityMatrix.Clone();
    private readonly PixelShaderUniformValue[] _uniforms = new PixelShaderUniformValue[9];
    private bool _identity = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="CtmStage"/> class with the identity matrix.
    /// </summary>
    /// <param name="shader">The compiled color matrix shader, or <see langword="null"/> when the renderer cannot compile it.</param>
    public CtmStage(IPixelShader? shader)
    {
        _shader = shader;
        Push();
    }

    /// <summary>
    /// Gets a value indicating whether the stage has a shader and can change the frame.
    /// </summary>
    public bool IsSupported => _shader is not null;

    /// <summary>
    /// Gets a value indicating whether the current matrix is exactly the identity.
    /// </summary>
    public bool IsIdentity => _identity;

    /// <summary>
    /// Gets the current matrix as nine row-major values.
    /// </summary>
    public ReadOnlySpan<double> Matrix => _matrix;

    /// <summary>
    /// Replaces the matrix. The stage skips the shader when the new matrix is exactly the identity.
    /// </summary>
    /// <param name="rowMajor3x3">Nine values in row-major order that map linear RGB to linear RGB.</param>
    /// <exception cref="ArgumentException"><paramref name="rowMajor3x3"/> does not have nine values.</exception>
    public void SetMatrix(ReadOnlySpan<double> rowMajor3x3)
    {
        if (rowMajor3x3.Length != 9)
        {
            throw new ArgumentException("A color transform matrix must have nine components.", nameof(rowMajor3x3));
        }

        _thread.Assert();
        rowMajor3x3.CopyTo(_matrix);
        _identity = _matrix.AsSpan().SequenceEqual(IdentityMatrix);
        Push();
    }

    /// <summary>
    /// Sets the matrix back to the identity.
    /// </summary>
    public void Reset() => SetMatrix(IdentityMatrix);

    /// <summary>
    /// Leaves the box where it is, because the stage draws the frame at its own position. Unlike the default, it always returns <see langword="true"/>.
    /// </summary>
    /// <param name="scene">The box in the composited frame, in physical pixels.</param>
    /// <param name="context">The frame size, timing and damage.</param>
    /// <param name="output">The same box as <paramref name="scene"/>.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public bool TryMapBox(in Box scene, in PostContext context, out Box output)
    {
        output = scene;
        return true;
    }

    /// <inheritdoc/>
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
