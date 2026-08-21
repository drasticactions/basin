using Basin;
using Basin.Render.Gl;
using Silk.NET.OpenGLES;

namespace TinyComp;

internal sealed class GlBlurEffect : IGlBackdropEffect, Basin.Capabilities.IBackgroundEffects, IDisposable
{
    private const int Levels = 3;

    private const float Offset = 1.5f;

    private const int Pad = 64;

    private const string VertexSource = """
        #version 300 es
        void main() {
            vec2 corner = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string DownSource = """
        #version 300 es
        precision highp float;
        uniform sampler2D src;
        uniform vec2 u_srcScale;
        uniform vec2 u_srcInvSize;
        uniform vec2 u_halfpixel;
        out vec4 color;
        void main() {
            vec2 uv = gl_FragCoord.xy * u_srcScale * u_srcInvSize;
            vec2 hp = u_halfpixel;
            vec4 sum = texture(src, uv) * 4.0;
            sum += texture(src, uv - hp);
            sum += texture(src, uv + hp);
            sum += texture(src, uv + vec2(hp.x, -hp.y));
            sum += texture(src, uv - vec2(hp.x, -hp.y));
            color = sum / 8.0;
        }
        """;

    private const string UpSource = """
        #version 300 es
        precision highp float;
        uniform sampler2D src;
        uniform vec2 u_srcScale;
        uniform vec2 u_srcInvSize;
        uniform vec2 u_halfpixel;
        out vec4 color;
        void main() {
            vec2 uv = gl_FragCoord.xy * u_srcScale * u_srcInvSize;
            vec2 hp = u_halfpixel;
            vec4 sum = texture(src, uv + vec2(-hp.x * 2.0, 0.0));
            sum += texture(src, uv + vec2(-hp.x, hp.y)) * 2.0;
            sum += texture(src, uv + vec2(0.0, hp.y * 2.0));
            sum += texture(src, uv + vec2(hp.x, hp.y)) * 2.0;
            sum += texture(src, uv + vec2(hp.x * 2.0, 0.0));
            sum += texture(src, uv + vec2(hp.x, -hp.y)) * 2.0;
            sum += texture(src, uv + vec2(0.0, -hp.y * 2.0));
            sum += texture(src, uv + vec2(-hp.x, -hp.y)) * 2.0;
            color = sum / 12.0;
        }
        """;

    private sealed class Program
    {
        public uint Handle;
        public int SrcScale;
        public int SrcInvSize;
        public int Halfpixel;
    }

    private sealed class Level
    {
        public uint Texture;
        public uint Fbo;
        public int Width;
        public int Height;
    }

    private sealed class Pyramid
    {
        public required Level[] Chain;
    }

    private readonly GlDevice _device;
    private readonly Program _down;
    private readonly Program _up;
    private readonly Dictionary<(int Width, int Height), Pyramid> _pyramids = [];

    public Basin.Capabilities.BackgroundEffects Supported => Basin.Capabilities.BackgroundEffects.Blur;

    public GlBlurEffect(GlDevice device)
    {
        _device = device;
        _down = Compile(DownSource);
        _up = Compile(UpSource);
    }

    public bool Record(in GlBackdropContext context, out GlBackdropResult result)
    {
        var gl = _device.Gl;
        var pyramid = PyramidFor(context.TargetWidth, context.TargetHeight);
        var padded = new Box(
                context.Bounds.X - Pad,
                context.Bounds.Y - Pad,
                context.Bounds.Width + (2 * Pad),
                context.Bounds.Height + (2 * Pad))
            .Intersect(new Box(0, 0, context.TargetWidth, context.TargetHeight));

        gl.Disable(EnableCap.Blend);
        gl.Enable(EnableCap.ScissorTest);
        for (var i = 1; i <= Levels; i++)
        {
            var srcTexture = i == 1 ? context.Backdrop : pyramid.Chain[i - 1].Texture;
            var srcWidth = i == 1 ? context.TargetWidth : pyramid.Chain[i - 1].Width;
            var srcHeight = i == 1 ? context.TargetHeight : pyramid.Chain[i - 1].Height;
            BlurPass(_down, pyramid.Chain[i], srcTexture, srcWidth, srcHeight, srcScale: 2f, RegionAtLevel(padded, i));
        }

        for (var i = Levels - 1; i >= 0; i--)
        {
            var src = pyramid.Chain[i + 1];
            BlurPass(_up, pyramid.Chain[i], src.Texture, src.Width, src.Height, srcScale: 0.5f, RegionAtLevel(padded, i));
        }

        gl.Disable(EnableCap.ScissorTest);
        var top = pyramid.Chain[0];
        result = new GlBackdropResult(top.Texture, top.Width, top.Height, context.Bounds);
        return true;
    }

    private static Box RegionAtLevel(in Box padded, int level)
    {
        var x = padded.X >> level;
        var y = padded.Y >> level;
        var right = ((padded.X + padded.Width) >> level) + 1;
        var bottom = ((padded.Y + padded.Height) >> level) + 1;
        return new Box(x, y, right - x, bottom - y);
    }

    private void BlurPass(Program program, Level dst, uint srcTexture, int srcWidth, int srcHeight, float srcScale, Box region)
    {
        var gl = _device.Gl;
        var width = Math.Min(region.Width, dst.Width - region.X);
        var height = Math.Min(region.Height, dst.Height - region.Y);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, dst.Fbo);
        gl.Viewport(0, 0, (uint)dst.Width, (uint)dst.Height);
        gl.Scissor(region.X, region.Y, (uint)width, (uint)height);
        gl.UseProgram(program.Handle);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, srcTexture);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.Uniform2(program.SrcScale, srcScale, srcScale);
        gl.Uniform2(program.SrcInvSize, 1f / srcWidth, 1f / srcHeight);
        gl.Uniform2(program.Halfpixel, Offset / srcWidth, Offset / srcHeight);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    private Pyramid PyramidFor(int width, int height)
    {
        if (_pyramids.TryGetValue((width, height), out var existing))
        {
            return existing;
        }

        var chain = new Level[Levels + 1];
        for (var i = 0; i <= Levels; i++)
        {
            chain[i] = CreateLevel(Math.Max(1, width >> i), Math.Max(1, height >> i));
        }

        var pyramid = new Pyramid { Chain = chain };
        _pyramids[(width, height)] = pyramid;
        return pyramid;
    }

    private Level CreateLevel(int width, int height)
    {
        var gl = _device.Gl;
        var level = new Level { Width = width, Height = height };
        level.Texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, level.Texture);
        gl.TexStorage2D(TextureTarget.Texture2D, 1, SizedInternalFormat.Rgba8, (uint)width, (uint)height);
        level.Fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, level.Fbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, level.Texture, 0);
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException("blur level framebuffer incomplete");
        }

        return level;
    }

    private Program Compile(string fragmentSource)
    {
        var gl = _device.Gl;
        var handle = gl.CreateProgram();
        foreach (var (type, source) in new[] { (ShaderType.VertexShader, VertexSource), (ShaderType.FragmentShader, fragmentSource) })
        {
            var shader = gl.CreateShader(type);
            gl.ShaderSource(shader, source);
            gl.CompileShader(shader);
            gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
            if (compiled == 0)
            {
                throw new InvalidOperationException($"{type}: {gl.GetShaderInfoLog(shader)}");
            }

            gl.AttachShader(handle, shader);
            gl.DeleteShader(shader);
        }

        gl.LinkProgram(handle);
        gl.GetProgram(handle, ProgramPropertyARB.LinkStatus, out var linked);
        if (linked == 0)
        {
            throw new InvalidOperationException($"link: {gl.GetProgramInfoLog(handle)}");
        }

        return new Program
        {
            Handle = handle,
            SrcScale = gl.GetUniformLocation(handle, "u_srcScale"),
            SrcInvSize = gl.GetUniformLocation(handle, "u_srcInvSize"),
            Halfpixel = gl.GetUniformLocation(handle, "u_halfpixel"),
        };
    }

    public void Dispose()
    {
        var gl = _device.Gl;
        foreach (var pyramid in _pyramids.Values)
        {
            foreach (var level in pyramid.Chain)
            {
                gl.DeleteFramebuffer(level.Fbo);
                gl.DeleteTexture(level.Texture);
            }
        }

        _pyramids.Clear();
        gl.DeleteProgram(_down.Handle);
        gl.DeleteProgram(_up.Handle);
    }
}
