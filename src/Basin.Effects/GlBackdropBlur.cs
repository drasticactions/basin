using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Silk.NET.OpenGLES;

namespace Basin.Effects;

public sealed class GlBackdropBlur : IGlBackdropEffect, IBackdropBlur
{
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

    private const string OnscreenSource = """
        #version 300 es
        precision highp float;
        uniform sampler2D src;
        uniform vec2 u_srcScale;
        uniform vec2 u_srcInvSize;
        uniform vec2 u_halfpixel;
        uniform vec4 u_colorMatrix0;
        uniform vec4 u_colorMatrix1;
        uniform vec4 u_colorMatrix2;
        uniform sampler2D u_noise;
        uniform vec2 u_noiseSize;
        uniform sampler2D u_plain;
        uniform vec2 u_targetSize;
        uniform vec4 u_box;
        uniform vec4 u_cornerRadius;
        uniform float u_opacity;
        uniform float u_intensity;
        uniform vec4 u_frost;
        out vec4 color;
        float basin_rounded_box(vec2 position, vec2 center, vec2 extents, vec4 radius) {
            vec2 p = position - center;
            float r = p.x > 0.0
                ? (p.y < 0.0 ? radius.y : radius.w)
                : (p.y < 0.0 ? radius.x : radius.z);
            vec2 q = abs(p) - extents + vec2(r);
            return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
        }
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
            vec4 blurred = sum / 12.0;
            vec4 base = vec4(mix(blurred.rgb, u_frost.rgb, u_frost.a), blurred.a);
            vec3 tinted = vec3(
                dot(base, u_colorMatrix0),
                dot(base, u_colorMatrix1),
                dot(base, u_colorMatrix2)) * u_intensity;
            float noise = texture(u_noise, gl_FragCoord.xy / u_noiseSize).r;
            vec3 result = tinted + vec3(noise);
            float coverage = u_opacity;
            if (u_cornerRadius != vec4(0.0)) {
                float f = basin_rounded_box(gl_FragCoord.xy, u_box.xy, u_box.zw, u_cornerRadius);
                float df = fwidth(f);
                coverage *= 1.0 - clamp(0.5 + f / df, 0.0, 1.0);
            }
            vec3 plain = texture(u_plain, gl_FragCoord.xy / u_targetSize).rgb;
            color = vec4(mix(plain, result, coverage), blurred.a);
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
        public int ColorMatrix0;
        public int ColorMatrix1;
        public int ColorMatrix2;
        public int Noise;
        public int NoiseSize;
        public int Plain;
        public int TargetSize;
        public int BoxUniform;
        public int CornerRadius;
        public int Opacity;
        public int Intensity;
        public int Frost;
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
    private readonly Program _onscreen;
    private readonly Dictionary<(int Width, int Height), Pyramid> _pyramids = [];
    private readonly float[] _colorMatrix = new float[BlurColorMatrix.Length];
    private uint _noiseTexture;
    private int _noiseStrength = -1;
    private int _noiseSide;
    private readonly Dictionary<object, BlurSurfaceOptions> _surfaces = [];
    private readonly float[] _surfaceMatrix = new float[BlurColorMatrix.Length];
    private uint _plainBackdrop;
    private int _targetWidth;
    private int _targetHeight;
    private Box _surface;
    private BlurSurfaceOptions _current = new();
    private BlurOptions _options = new();
    private BlurStrength _strength = BlurStrength.For(new BlurOptions().Strength);
    private bool _disposed;

    public BackgroundEffects Supported => BackgroundEffects.Blur | BackgroundEffects.Contrast;

    public int ExpandSize => _strength.ExpandSize;

    public BlurCorners Corners { get; set; }

    public double Opacity { get; set; } = 1.0;

    public void SetSurface(object key, in BlurSurfaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(key);
        _surfaces[key] = options;
    }

    public bool ForgetSurface(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _surfaces.Remove(key);
    }

    public BlurOptions Options
    {
        get => _options;
        set
        {
            var iterations = _strength.Iterations;
            _options = value;
            _strength = BlurStrength.For(value.Strength);
            BlurColorMatrix.Build(value.Saturation, value.Contrast, _colorMatrix);
            EnsureNoise();
            if (_strength.Iterations != iterations)
            {
                DropPyramids();
            }
        }
    }

    public GlBackdropBlur(GlDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _down = Compile(DownSource);
        _up = Compile(UpSource);
        _onscreen = Compile(OnscreenSource);
        BlurColorMatrix.Build(_options.Saturation, _options.Contrast, _colorMatrix);
        EnsureNoise();
        BasinCounters.Track();
    }

    public bool Record(in GlBackdropContext context, out GlBackdropResult result)
    {
        var gl = _device.Gl;
        var levels = _strength.Iterations;
        var pad = _strength.ExpandSize;
        var pyramid = PyramidFor(context.TargetWidth, context.TargetHeight);
        var padded = new Box(
                context.Bounds.X - pad,
                context.Bounds.Y - pad,
                context.Bounds.Width + (2 * pad),
                context.Bounds.Height + (2 * pad))
            .Intersect(new Box(0, 0, context.TargetWidth, context.TargetHeight));

        _plainBackdrop = context.Backdrop;
        _targetWidth = context.TargetWidth;
        _targetHeight = context.TargetHeight;
        _surface = context.Bounds;
        _current = context.Key is { } key && _surfaces.TryGetValue(key, out var stored) ? stored : new BlurSurfaceOptions();
        BuildSurfaceMatrix();
        gl.Disable(EnableCap.Blend);
        gl.Enable(EnableCap.ScissorTest);
        if (!_current.Blur)
        {
            BlurPass(
                _onscreen, pyramid.Chain[0], context.Backdrop, context.TargetWidth, context.TargetHeight,
                srcScale: 1f, RegionAtLevel(padded, 0), plain: true);
            gl.Disable(EnableCap.ScissorTest);
            var flat = pyramid.Chain[0];
            result = new GlBackdropResult(flat.Texture, flat.Width, flat.Height, context.Bounds);
            return true;
        }

        for (var i = 1; i <= levels; i++)
        {
            var srcTexture = i == 1 ? context.Backdrop : pyramid.Chain[i - 1].Texture;
            var srcWidth = i == 1 ? context.TargetWidth : pyramid.Chain[i - 1].Width;
            var srcHeight = i == 1 ? context.TargetHeight : pyramid.Chain[i - 1].Height;
            BlurPass(_down, pyramid.Chain[i], srcTexture, srcWidth, srcHeight, srcScale: 2f, RegionAtLevel(padded, i));
        }

        for (var i = levels - 1; i >= 0; i--)
        {
            var src = pyramid.Chain[i + 1];
            BlurPass(
                i == 0 ? _onscreen : _up,
                pyramid.Chain[i], src.Texture, src.Width, src.Height, srcScale: 0.5f, RegionAtLevel(padded, i));
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

    private void BlurPass(Program program, Level dst, uint srcTexture, int srcWidth, int srcHeight, float srcScale, Box region, bool plain = false)
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
        var halfpixel = plain ? 0f : (float)(0.5 * _strength.Offset);
        gl.Uniform2(program.Halfpixel, halfpixel / srcWidth, halfpixel / srcHeight);
        if (program.ColorMatrix0 >= 0)
        {
            gl.Uniform4(program.ColorMatrix0, _surfaceMatrix[0], _surfaceMatrix[1], _surfaceMatrix[2], _surfaceMatrix[3]);
            gl.Uniform4(program.ColorMatrix1, _surfaceMatrix[4], _surfaceMatrix[5], _surfaceMatrix[6], _surfaceMatrix[7]);
            gl.Uniform4(program.ColorMatrix2, _surfaceMatrix[8], _surfaceMatrix[9], _surfaceMatrix[10], _surfaceMatrix[11]);
            var noiseSize = (float)BlurNoise.SizeFor(_options.NoiseScale);
            gl.Uniform1(program.Noise, 1);
            gl.Uniform2(program.NoiseSize, noiseSize, noiseSize);
            gl.ActiveTexture(TextureUnit.Texture1);
            gl.BindTexture(TextureTarget.Texture2D, _noiseTexture);
            gl.Uniform1(program.Plain, 2);
            gl.ActiveTexture(TextureUnit.Texture2);
            gl.BindTexture(TextureTarget.Texture2D, _plainBackdrop);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.Uniform2(program.TargetSize, (float)_targetWidth, _targetHeight);
            gl.Uniform4(
                program.BoxUniform,
                (float)(_surface.X + (_surface.Width / 2.0)),
                (float)(_surface.Y + (_surface.Height / 2.0)),
                (float)(_surface.Width / 2.0),
                (float)(_surface.Height / 2.0));
            var corners = _current.Corners.IsSquare ? Corners : _current.Corners;
            gl.Uniform4(
                program.CornerRadius,
                (float)corners.TopLeft,
                (float)corners.TopRight,
                (float)corners.BottomLeft,
                (float)corners.BottomRight);
            gl.Uniform1(program.Opacity, (float)Math.Clamp(Opacity * _current.Opacity, 0, 1));
            var parameters = _current.ContrastParameters;
            gl.Uniform1(program.Intensity, _current.Contrast ? (float)parameters.Intensity : 1f);
            if (_current.Contrast && parameters.Frost)
            {
                var frost = parameters.FrostColor;
                gl.Uniform4(
                    program.Frost,
                    ((frost >> 16) & 0xFF) / 255f,
                    ((frost >> 8) & 0xFF) / 255f,
                    (frost & 0xFF) / 255f,
                    ((frost >> 24) & 0xFF) / 255f);
            }
            else
            {
                gl.Uniform4(program.Frost, 0f, 0f, 0f, 0f);
            }
        }

        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    private Pyramid PyramidFor(int width, int height)
    {
        if (_pyramids.TryGetValue((width, height), out var existing))
        {
            return existing;
        }

        var chain = new Level[_strength.Iterations + 1];
        for (var i = 0; i <= _strength.Iterations; i++)
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
            ColorMatrix0 = gl.GetUniformLocation(handle, "u_colorMatrix0"),
            ColorMatrix1 = gl.GetUniformLocation(handle, "u_colorMatrix1"),
            ColorMatrix2 = gl.GetUniformLocation(handle, "u_colorMatrix2"),
            Noise = gl.GetUniformLocation(handle, "u_noise"),
            NoiseSize = gl.GetUniformLocation(handle, "u_noiseSize"),
            Plain = gl.GetUniformLocation(handle, "u_plain"),
            TargetSize = gl.GetUniformLocation(handle, "u_targetSize"),
            BoxUniform = gl.GetUniformLocation(handle, "u_box"),
            CornerRadius = gl.GetUniformLocation(handle, "u_cornerRadius"),
            Opacity = gl.GetUniformLocation(handle, "u_opacity"),
            Intensity = gl.GetUniformLocation(handle, "u_intensity"),
            Frost = gl.GetUniformLocation(handle, "u_frost"),
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        var gl = _device.Gl;
        DropPyramids();
        gl.DeleteProgram(_down.Handle);
        gl.DeleteProgram(_up.Handle);
        gl.DeleteProgram(_onscreen.Handle);
        if (_noiseTexture != 0)
        {
            gl.DeleteTexture(_noiseTexture);
            _noiseTexture = 0;
        }
    }

    private void BuildSurfaceMatrix()
    {
        if (!_current.Contrast)
        {
            _colorMatrix.CopyTo(_surfaceMatrix, 0);
            return;
        }

        var parameters = _current.ContrastParameters;
        BlurColorMatrix.Build(parameters.Saturation, parameters.Contrast, _surfaceMatrix);
    }

    private void EnsureNoise()
    {
        var strength = Math.Max(0, _options.NoiseStrength);
        var side = BlurNoise.SizeFor(_options.NoiseScale);
        if (_noiseTexture != 0 && _noiseStrength == strength && _noiseSide == side)
        {
            return;
        }

        var gl = _device.Gl;
        if (_noiseTexture != 0 && _noiseSide != side)
        {
            gl.DeleteTexture(_noiseTexture);
            _noiseTexture = 0;
        }

        _noiseStrength = strength;
        _noiseSide = side;
        if (_noiseTexture == 0)
        {
            _noiseTexture = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, _noiseTexture);
            gl.TexStorage2D(TextureTarget.Texture2D, 1, SizedInternalFormat.R8, (uint)side, (uint)side);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        }
        else
        {
            gl.BindTexture(TextureTarget.Texture2D, _noiseTexture);
        }

        var pixels = new byte[side * side];
        BlurNoise.Fill(pixels, strength, _options.NoiseScale);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.TexSubImage2D<byte>(
            TextureTarget.Texture2D, 0, 0, 0, (uint)side, (uint)side,
            PixelFormat.Red, PixelType.UnsignedByte, pixels);
    }

    private void DropPyramids()
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
    }
}
