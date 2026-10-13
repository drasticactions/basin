using Mesa.Egl;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

internal sealed unsafe class GlYuvConverter : IDisposable
{
    private readonly GlDevice _device;
    private readonly ShaderProgram _program;
    private readonly int _rect;
    private readonly int _coeffs;
    private readonly Dictionary<IBuffer, Planes> _planes = [];

    private sealed class Planes
    {
        public required EglImage Luma;
        public required EglImage Chroma;
        public uint LumaTexture;
        public uint ChromaTexture;
        public uint LumaFramebuffer;
        public uint ChromaFramebuffer;
        public int Width;
        public int Height;
        public Action? Forget;
    }

    public GlYuvConverter(GlDevice device)
    {
        _device = device;
        _program = new ShaderProgram(device.Gl, GlShaders.Vertex, GlShaders.YuvFragment);
        _rect = device.Gl.GetUniformLocation(_program.Program, "u_rect");
        _coeffs = device.Gl.GetUniformLocation(_program.Program, "u_coeffs");
    }

    public bool Convert(
        uint vao, in GlTextureView source, in FBox sourceBox, IBuffer target, YuvCoefficients coefficients, YuvRange range)
    {
        if (PlanesFor(target) is not { } planes)
        {
            return false;
        }

        var gl = _device.Gl;
        var crop = sourceBox.IsEmpty ? new FBox(0, 0, source.Width, source.Height) : sourceBox;
        var fit = YuvConversion.Fit((int)crop.Width, (int)crop.Height, planes.Width, planes.Height);
        var (kr, kb) = YuvConversion.Weights(coefficients);
        gl.BindVertexArray(vao);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.ScissorTest);
        gl.UseProgram(_program.Program);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture((TextureTarget)source.Target, source.Name);
        gl.TexParameter((TextureTarget)source.Target, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter((TextureTarget)source.Target, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.Uniform4(
            _program.Src,
            (float)(crop.X / source.Width), (float)(crop.Y / source.Height),
            (float)(crop.Width / source.Width), (float)(crop.Height / source.Height));
        gl.Uniform4(_rect, (float)fit.X, fit.Y, fit.Width, fit.Height);
        var identity = stackalloc float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        gl.UniformMatrix3(_program.Transform, 1, false, identity);
        Draw(planes.LumaFramebuffer, planes.Width, planes.Height, kr, kb, range, chroma: false);
        Draw(planes.ChromaFramebuffer, (planes.Width + 1) / 2, (planes.Height + 1) / 2, kr, kb, range, chroma: true);
        gl.Finish();
        return true;
    }

    private void Draw(uint framebuffer, int width, int height, float kr, float kb, YuvRange range, bool chroma)
    {
        var gl = _device.Gl;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.Viewport(0, 0, (uint)width, (uint)height);
        gl.Uniform4(_program.Dst, 0f, 0f, (float)width, height);
        gl.Uniform2(_program.Target, (float)width, height);
        gl.Uniform4(_coeffs, kr, kb, range == YuvRange.Limited ? 1f : 0f, chroma ? 1f : 0f);
        gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
    }

    private Planes? PlanesFor(IBuffer target)
    {
        if (_planes.TryGetValue(target, out var cached))
        {
            return cached;
        }

        Basin.Diagnostics.AllocationScope.Pause();
        try
        {
            return ImportPlanes(target);
        }
        finally
        {
            Basin.Diagnostics.AllocationScope.Resume();
        }
    }

    private Planes? ImportPlanes(IBuffer target)
    {

        if (!target.TryGetDmabuf(out var attributes) ||
            !YuvConversion.TrySplit(attributes, out var luma, out var chroma) ||
            _device.ImportDmabufImage(luma) is not { } lumaImage)
        {
            return null;
        }

        if (_device.ImportDmabufImage(chroma) is not { } chromaImage)
        {
            lumaImage.Dispose();
            return null;
        }

        var planes = new Planes { Luma = lumaImage, Chroma = chromaImage, Width = attributes.Width, Height = attributes.Height };
        (planes.LumaTexture, planes.LumaFramebuffer) = Attach(lumaImage);
        (planes.ChromaTexture, planes.ChromaFramebuffer) = Attach(chromaImage);
        planes.Forget = () => Drop(target);
        target.Destroyed += planes.Forget;
        _planes[target] = planes;
        return planes;
    }

    private (uint Texture, uint Framebuffer) Attach(EglImage image)
    {
        var gl = _device.Gl;
        var texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        _device.BindImageToTexture2D(image);
        var framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
        return (texture, framebuffer);
    }

    private void Drop(IBuffer target)
    {
        if (!_planes.Remove(target, out var planes))
        {
            return;
        }

        Release(planes);
    }

    private void Release(Planes planes)
    {
        var gl = _device.Gl;
        gl.DeleteFramebuffer(planes.LumaFramebuffer);
        gl.DeleteFramebuffer(planes.ChromaFramebuffer);
        gl.DeleteTexture(planes.LumaTexture);
        gl.DeleteTexture(planes.ChromaTexture);
        planes.Luma.Dispose();
        planes.Chroma.Dispose();
    }

    public void Dispose()
    {
        foreach (var (target, planes) in _planes)
        {
            target.Destroyed -= planes.Forget;
            Release(planes);
        }

        _planes.Clear();
        _program.Dispose(_device.Gl);
    }
}
