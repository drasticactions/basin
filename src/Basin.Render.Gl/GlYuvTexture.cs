using Mesa.Egl;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

/// <summary>
/// Represents a linear NV12 dmabuf that is converted into an RGBA texture on the GPU. The conversion runs again after <see cref="MarkDirty"/>.
/// </summary>
public sealed unsafe class GlYuvTexture : ITexture, IRefreshableTexture
{
    private const GLEnum FramebufferBinding = (GLEnum)0x8CA6;
    private const GLEnum ViewportParameter = (GLEnum)0x0BA2;
    private const GLEnum CurrentProgram = (GLEnum)0x8B8D;
    private const GLEnum VertexArrayBinding = (GLEnum)0x85B5;
    private const GLEnum ActiveTextureParameter = (GLEnum)0x84E0;
    private const GLEnum TextureBinding2D = (GLEnum)0x8069;

    private readonly GlDevice _device;
    private readonly GlNvProgram _program;
    private readonly EglImage _luma;
    private readonly EglImage _chroma;
    private readonly uint _lumaTexture;
    private readonly uint _chromaTexture;
    private readonly uint _output;
    private readonly uint _framebuffer;
    private bool _dirty = true;

    private GlYuvTexture(GlDevice device, GlNvProgram program, EglImage luma, EglImage chroma, in DmabufAttributes attributes)
    {
        _device = device;
        _program = program;
        _luma = luma;
        _chroma = chroma;
        Attributes = attributes;
        Width = attributes.Width;
        Height = attributes.Height;
        var gl = device.Gl;
        _lumaTexture = PlaneTexture(luma);
        _chromaTexture = PlaneTexture(chroma);
        _output = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _output);
        gl.TexStorage2D(TextureTarget.Texture2D, 1, SizedInternalFormat.Rgba8, (uint)Width, (uint)Height);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _output, 0);
    }

    /// <summary>
    /// Gets the dmabuf that the texture was imported from. The imported buffer owns its fds.
    /// </summary>
    public DmabufAttributes Attributes { get; }

    /// <inheritdoc/>
    public int Width { get; }

    /// <inheritdoc/>
    public int Height { get; }

    /// <summary>
    /// Gets a value indicating whether the texture has alpha. It is always <see langword="false"/>.
    /// </summary>
    public bool HasAlpha => false;

    /// <summary>
    /// Gets or sets the YUV matrix of the conversion. The default is BT.601. A change runs the conversion again at the next <see cref="Acquire"/>.
    /// </summary>
    public YuvCoefficients Coefficients
    {
        get => _coefficients;
        set
        {
            _dirty |= value != _coefficients;
            _coefficients = value;
        }
    }

    /// <summary>
    /// Gets or sets the quantization range of the source. The default is limited range. A change runs the conversion again at the next <see cref="Acquire"/>.
    /// </summary>
    public YuvRange Range
    {
        get => _range;
        set
        {
            _dirty |= value != _range;
            _range = value;
        }
    }

    private YuvCoefficients _coefficients = YuvCoefficients.Bt601;
    private YuvRange _range = YuvRange.Limited;

    internal static GlYuvTexture? TryImport(GlDevice device, GlNvProgram program, in DmabufAttributes attributes)
    {
        if (!YuvConversion.TrySplit(attributes, out var luma, out var chroma) ||
            device.ImportDmabufImage(luma) is not { } lumaImage)
        {
            return null;
        }

        if (device.ImportDmabufImage(chroma) is not { } chromaImage)
        {
            lumaImage.Dispose();
            return null;
        }

        var gl = device.Gl;
        var framebuffer = 0;
        gl.GetInteger(FramebufferBinding, &framebuffer);
        try
        {
            return new GlYuvTexture(device, program, lumaImage, chromaImage, attributes);
        }
        finally
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
        }
    }

    /// <inheritdoc/>
    public void MarkDirty() => _dirty = true;

    /// <summary>
    /// Converts the planes into the RGBA texture when the content is out of date and gets its name.
    /// </summary>
    /// <param name="name">The name of the RGBA 2D texture. The texture owns it.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public bool Acquire(out uint name)
    {
        name = _output;
        if (_dirty)
        {
            Convert();
            _dirty = false;
        }

        return true;
    }

    private uint PlaneTexture(EglImage image)
    {
        var gl = _device.Gl;
        var texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        _device.BindImageToTexture2D(image);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return texture;
    }

    private void Convert()
    {
        var gl = _device.Gl;
        int framebuffer = 0, program = 0, vao = 0, active = 0, unit0 = 0, unit1 = 0;
        var viewport = stackalloc int[4];
        gl.GetInteger(FramebufferBinding, &framebuffer);
        gl.GetInteger(ViewportParameter, viewport);
        gl.GetInteger(CurrentProgram, &program);
        gl.GetInteger(VertexArrayBinding, &vao);
        gl.GetInteger(ActiveTextureParameter, &active);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.GetInteger(TextureBinding2D, &unit0);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.GetInteger(TextureBinding2D, &unit1);
        var blend = gl.IsEnabled(EnableCap.Blend);
        var scissor = gl.IsEnabled(EnableCap.ScissorTest);

        gl.BindTexture(TextureTarget.Texture2D, _chromaTexture);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, _lumaTexture);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.Viewport(0, 0, (uint)Width, (uint)Height);
        gl.BindVertexArray(_program.Vao);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.ScissorTest);
        _program.Draw(gl, Width, Height, Coefficients, Range);

        var fence = _device.ExportFence();
        if (fence >= 0)
        {
            RenderFences.PublishFenceTo(Attributes, forWrite: false, fence);
            RenderFences.CloseFence(fence);
        }
        else
        {
            gl.Flush();
        }

        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(TextureTarget.Texture2D, (uint)unit1);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, (uint)unit0);
        gl.ActiveTexture((TextureUnit)active);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
        gl.Viewport(viewport[0], viewport[1], (uint)viewport[2], (uint)viewport[3]);
        gl.UseProgram((uint)program);
        gl.BindVertexArray((uint)vao);
        if (blend)
        {
            gl.Enable(EnableCap.Blend);
        }

        if (scissor)
        {
            gl.Enable(EnableCap.ScissorTest);
        }
    }

    /// <summary>
    /// Deletes the GL objects and destroys both EGL images.
    /// </summary>
    public void Dispose()
    {
        var gl = _device.Gl;
        gl.DeleteFramebuffer(_framebuffer);
        gl.DeleteTexture(_output);
        gl.DeleteTexture(_lumaTexture);
        gl.DeleteTexture(_chromaTexture);
        _luma.Dispose();
        _chroma.Dispose();
    }
}
