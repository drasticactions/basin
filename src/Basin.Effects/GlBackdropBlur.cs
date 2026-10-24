using System.Numerics;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Silk.NET.OpenGLES;

namespace Basin.Effects;

/// <summary>
/// The backdrop blur on a GL device. It keeps one mip pyramid for each target size and drops a pyramid when no backdrop of that size remains.
/// </summary>
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
        uniform vec4 u_srcClamp;
        out vec4 color;
        vec4 tap(sampler2D s, vec2 uv) { return texture(s, clamp(uv, u_srcClamp.xy, u_srcClamp.zw)); }
        void main() {
            vec2 uv = gl_FragCoord.xy * u_srcScale * u_srcInvSize;
            vec2 hp = u_halfpixel;
            vec4 sum = tap(src, uv) * 4.0;
            sum += tap(src, uv - hp);
            sum += tap(src, uv + hp);
            sum += tap(src, uv + vec2(hp.x, -hp.y));
            sum += tap(src, uv - vec2(hp.x, -hp.y));
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
        uniform vec4 u_srcClamp;
        uniform sampler2D u_plain;
        uniform vec2 u_targetSize;
        uniform vec4 u_box;
        uniform vec4 u_cornerRadius;
        uniform float u_opacity;
        uniform float u_intensity;
        uniform vec4 u_frost;
        out vec4 color;
        vec4 tap(sampler2D s, vec2 uv) { return texture(s, clamp(uv, u_srcClamp.xy, u_srcClamp.zw)); }
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
            vec4 sum = tap(src, uv + vec2(-hp.x * 2.0, 0.0));
            sum += tap(src, uv + vec2(-hp.x, hp.y)) * 2.0;
            sum += tap(src, uv + vec2(0.0, hp.y * 2.0));
            sum += tap(src, uv + vec2(hp.x, hp.y)) * 2.0;
            sum += tap(src, uv + vec2(hp.x * 2.0, 0.0));
            sum += tap(src, uv + vec2(hp.x, -hp.y)) * 2.0;
            sum += tap(src, uv + vec2(0.0, -hp.y * 2.0));
            sum += tap(src, uv + vec2(-hp.x, -hp.y)) * 2.0;
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
        uniform vec4 u_srcClamp;
        out vec4 color;
        vec4 tap(sampler2D s, vec2 uv) { return texture(s, clamp(uv, u_srcClamp.xy, u_srcClamp.zw)); }
        void main() {
            vec2 uv = gl_FragCoord.xy * u_srcScale * u_srcInvSize;
            vec2 hp = u_halfpixel;
            vec4 sum = tap(src, uv + vec2(-hp.x * 2.0, 0.0));
            sum += tap(src, uv + vec2(-hp.x, hp.y)) * 2.0;
            sum += tap(src, uv + vec2(0.0, hp.y * 2.0));
            sum += tap(src, uv + vec2(hp.x, hp.y)) * 2.0;
            sum += tap(src, uv + vec2(hp.x * 2.0, 0.0));
            sum += tap(src, uv + vec2(hp.x, -hp.y)) * 2.0;
            sum += tap(src, uv + vec2(0.0, -hp.y * 2.0));
            sum += tap(src, uv + vec2(-hp.x, -hp.y)) * 2.0;
            color = sum / 12.0;
        }
        """;

    private const string DwmDownSource = """
        #version 300 es
        precision highp float;
        uniform sampler2D src;
        uniform vec2 u_srcScale;
        uniform vec2 u_srcInvSize;
        uniform vec4 u_srcClamp;
        out vec4 color;
        vec4 tap(vec2 uv) { return texture(src, clamp(uv, u_srcClamp.xy, u_srcClamp.zw)); }
        void main() {
            vec2 uv = gl_FragCoord.xy * u_srcScale * u_srcInvSize;
            vec2 h = 0.5 * u_srcInvSize;
            color = (tap(uv + vec2(-h.x, -h.y)) + tap(uv + vec2(h.x, -h.y)) + tap(uv + vec2(-h.x, h.y)) + tap(uv + vec2(h.x, h.y))) * 0.25;
        }
        """;

    private const string DwmBlurSource = """
        #version 300 es
        precision highp float;
        uniform sampler2D src;
        uniform vec2 u_srcScale;
        uniform vec2 u_srcInvSize;
        uniform vec4 u_srcClamp;
        uniform sampler2D u_plain;
        uniform vec2 u_targetSize;
        uniform vec4 u_box;
        uniform vec4 u_cornerRadius;
        uniform float u_opacity;
        out vec4 color;
        const float W[16] = float[16](
            0.02800, 0.04784, 0.05251, 0.03851,
            0.04784, 0.08168, 0.09191, 0.06534,
            0.05251, 0.09101, 0.10268, 0.07235,
            0.03851, 0.06534, 0.07235, 0.05251);
        const float O[4] = float[4](-1.824, -0.608, 0.608, 1.824);
        vec4 tap(vec2 uv) { return texture(src, clamp(uv, u_srcClamp.xy, u_srcClamp.zw)); }
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
            vec4 sum = vec4(0.0);
            for (int r = 0; r < 4; r++) {
                for (int c = 0; c < 4; c++) {
                    sum += W[r * 4 + c] * tap(uv + vec2(O[c], O[r]) * u_srcInvSize);
                }
            }
            float coverage = u_opacity;
            if (u_cornerRadius != vec4(0.0)) {
                float f = basin_rounded_box(gl_FragCoord.xy, u_box.xy, u_box.zw, u_cornerRadius);
                float df = fwidth(f);
                coverage *= 1.0 - clamp(0.5 + f / df, 0.0, 1.0);
            }
            vec3 plain = texture(u_plain, gl_FragCoord.xy / u_targetSize).rgb;
            color = vec4(mix(plain, sum.rgb, coverage), 1.0);
        }
        """;

    private sealed class Program
    {
        public uint Handle;
        public int SrcScale;
        public int SrcInvSize;
        public int Halfpixel;
        public int SrcClamp;
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
        public int PcSource;
        public int PcSourcePad;
        public int PcDestination;
        public int PcAxis;
        public int PcClamp;
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
        public required int Width;
        public required int Height;
        public required Level Top;
        public Level[]? Chain;
        public readonly Dictionary<(int X, int Y), Level> Reduced = [];
        public readonly Dictionary<(int X, int Y), Level> Filtered = [];
        public Level? Filtered0;
        public Level? Reconstructed;
        public Level? Masked;
    }

    private readonly GlDevice _device;
    private readonly Program _down;
    private readonly Program _up;
    private readonly Program _onscreen;
    private readonly Program _dwmDown;
    private readonly Program _dwmBlur;
    private readonly Program _smolDown;
    private readonly Program _smolDown4;
    private readonly Program _smolGauss;
    private readonly Program _smolReconstruct;
    private readonly uint _maskProgram;
    private readonly uint _maskMeshProgram;
    private uint _maskVao;
    private uint _maskVbo;
    private float[] _maskData = [];
    private readonly Dictionary<(int Width, int Height), Pyramid> _pyramids = [];
    private readonly Dictionary<uint, (int Width, int Height)> _backdrops = [];
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
    private BlurStrength _widest = BlurStrength.For(new BlurOptions().Strength);
    private BlurStrength _active = BlurStrength.For(new BlurOptions().Strength);
    private int _depth = BlurStrength.For(new BlurOptions().Strength).Iterations;
    private bool _disposed;

    /// <summary>
    /// Gets blur and contrast, the two effects that this blur can draw.
    /// </summary>
    public BackgroundEffects Supported => BackgroundEffects.Blur | BackgroundEffects.Contrast;

    /// <summary>
    /// Gets the widest sample reach in pixels among the shared options and every key. It is at least 8 once any key asks for <see cref="BlurKernel.VistaDwm"/>.
    /// </summary>
    public int ExpandSize => Math.Max(_widest.ExpandSize, _anyVistaDwm ? VistaDwmKernel.ExpandSize : 0);

    private bool _anyVistaDwm;

    /// <inheritdoc/>
    public BlurCorners Corners { get; set; }

    /// <inheritdoc/>
    public double Opacity { get; set; } = 1.0;

    /// <inheritdoc/>
    public void SetSurface(object key, in BlurSurfaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(key);
        _surfaces[key] = options;
        _anyVistaDwm |= options.Kernel == BlurKernel.VistaDwm;
        if (options.Strength is { } strength)
        {
            Widen(BlurStrength.For(strength));
        }
    }

    /// <summary>
    /// Drops the options of one key. It does not make the pyramid shallower.
    /// </summary>
    /// <param name="key">The key that <see cref="SetSurface"/> took.</param>
    /// <returns><see langword="true"/> when the key had options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool ForgetSurface(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _surfaces.Remove(key);
    }

    /// <inheritdoc/>
    public BlurOptions Options
    {
        get => _options;
        set
        {
            _options = value;
            _anyVistaDwm |= value.Kernel == BlurKernel.VistaDwm;
            _strength = BlurStrength.For(value.Strength);
            BlurColorMatrix.Build(value.Saturation, value.Contrast, _colorMatrix);
            EnsureNoise();
            _widest = _strength;
            foreach (var surface in _surfaces.Values)
            {
                if (surface.Strength is { } strength && BlurStrength.For(strength).Iterations > _widest.Iterations)
                {
                    _widest = BlurStrength.For(strength);
                }
            }

            if (_widest.Iterations != _depth)
            {
                _depth = _widest.Iterations;
                DropPyramids();
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GlBackdropBlur"/> class and compiles its programs on the device.
    /// </summary>
    /// <param name="device">The GL device. Its context must be current.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public GlBackdropBlur(GlDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _down = Compile(DownSource);
        _up = Compile(UpSource);
        _onscreen = Compile(OnscreenSource);
        _dwmDown = Compile(DwmDownSource);
        _dwmBlur = Compile(DwmBlurSource);
        _smolDown = Compile(SmolGaussianShaders.GlHeader + SmolGaussianShaders.Down);
        _smolDown4 = Compile(SmolGaussianShaders.GlHeader + SmolGaussianShaders.Down4);
        _smolGauss = Compile(SmolGaussianShaders.GlHeader + SmolGaussianShaders.Gauss);
        _smolReconstruct = Compile(SmolGaussianShaders.GlHeader + SmolGaussianShaders.Reconstruct);
        _maskProgram = Compile(BlurMaskShaders.GlHeader + BlurMaskShaders.Body).Handle;
        _maskMeshProgram = Compile(BlurMaskShaders.GlHeader + BlurMaskShaders.Body, GlassShaders.GlMeshVertex).Handle;
        BlurColorMatrix.Build(_options.Saturation, _options.Contrast, _colorMatrix);
        EnsureNoise();
        _device.TextureDeleting += ForgetTexture;
        BasinCounters.Track();
    }

    internal int PyramidCount => _pyramids.Count;

    /// <summary>
    /// Blurs the backdrop under the bounds of the context with the options of its key. With a mesh, the result draws only through the triangles.
    /// </summary>
    /// <param name="context">The backdrop texture, the target size, the bounds, the key and the optional mesh.</param>
    /// <param name="result">The texture that holds the result and the box to composite from it.</param>
    /// <returns><see langword="true"/> when the result is ready to composite.</returns>
    public bool Record(in GlBackdropContext context, out GlBackdropResult result)
    {
        if (!RecordBlur(context, out result))
        {
            return false;
        }

        var mesh = context.Mesh.Span;
        if (mesh.IsEmpty)
        {
            return true;
        }

        var pyramid = PyramidFor(context.TargetWidth, context.TargetHeight);
        var masked = pyramid.Masked ??= CreateLevel(pyramid.Width, pyramid.Height);
        var bounds = result.Source;
        var gl = _device.Gl;
        gl.Disable(EnableCap.Blend);
        gl.Enable(EnableCap.ScissorTest);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, masked.Fbo);
        gl.Viewport(0, 0, (uint)masked.Width, (uint)masked.Height);
        gl.Scissor(bounds.X, bounds.Y, (uint)bounds.Width, (uint)bounds.Height);
        gl.UseProgram(_maskProgram);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, context.Backdrop);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        DrawMask(mesh, result.Texture, masked);
        gl.Disable(EnableCap.ScissorTest);
        result = new GlBackdropResult(masked.Texture, masked.Width, masked.Height, bounds);
        return true;
    }

    private unsafe void DrawMask(ReadOnlySpan<MeshVertex> mesh, uint source, Level target)
    {
        var gl = _device.Gl;
        if (_maskVao == 0)
        {
            _maskVao = gl.GenVertexArray();
            _maskVbo = gl.GenBuffer();
        }

        var floats = mesh.Length * 4;
        if (_maskData.Length < floats)
        {
            _maskData = new float[Math.Max(floats, _maskData.Length * 2)];
        }

        for (var i = 0; i < mesh.Length; i++)
        {
            var vertex = mesh[i];
            _maskData[(i * 4) + 0] = (vertex.X / target.Width * 2f) - 1f;
            _maskData[(i * 4) + 1] = (vertex.Y / target.Height * 2f) - 1f;
            _maskData[(i * 4) + 2] = vertex.U;
            _maskData[(i * 4) + 3] = vertex.V;
        }

        gl.BindVertexArray(_maskVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _maskVbo);
        fixed (float* data = _maskData)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(floats * sizeof(float)), data, BufferUsageARB.StreamDraw);
        }

        gl.EnableVertexAttribArray(0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        gl.UseProgram(_maskMeshProgram);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, source);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)mesh.Length);
        gl.DisableVertexAttribArray(0);
        gl.DisableVertexAttribArray(1);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        gl.BindVertexArray(0);
    }

    private bool RecordBlur(in GlBackdropContext context, out GlBackdropResult result)
    {
        var gl = _device.Gl;
        _current = context.Key is { } key && _surfaces.TryGetValue(key, out var stored) ? stored : new BlurSurfaceOptions();
        _active = _current.Strength is { } strength ? BlurStrength.For(strength) : _strength;
        var kernel = _current.Kernel ?? _options.Kernel;
        var levels = _active.Iterations;
        var pad = kernel == BlurKernel.VistaDwm ? VistaDwmKernel.ExpandSize : _active.ExpandSize;
        TrackBackdrop(context.Backdrop, context.TargetWidth, context.TargetHeight);
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
        BuildSurfaceMatrix();
        gl.Disable(EnableCap.Blend);
        gl.Enable(EnableCap.ScissorTest);
        if (!_current.Blur)
        {
            BlurPass(
                _onscreen, pyramid.Top, context.Backdrop, context.TargetWidth, context.TargetHeight,
                srcScale: 1f, RegionAtLevel(padded, 0), context.Bounds, plain: true);
            gl.Disable(EnableCap.ScissorTest);
            var flat = pyramid.Top;
            result = new GlBackdropResult(flat.Texture, flat.Width, flat.Height, context.Bounds);
            return true;
        }

        if (kernel == BlurKernel.SmolGaussian)
        {
            RecordSmol(context, pyramid);
            gl.Disable(EnableCap.ScissorTest);
            var smol = pyramid.Top;
            result = new GlBackdropResult(smol.Texture, smol.Width, smol.Height, context.Bounds);
            return true;
        }

        var chain = ChainOf(pyramid);
        if (kernel == BlurKernel.VistaDwm)
        {
            BlurPass(
                _dwmDown, chain[1], context.Backdrop, context.TargetWidth, context.TargetHeight, srcScale: 2f,
                RegionAtLevel(padded, 1), context.Bounds);
            var half = chain[1];
            BlurPass(
                _dwmBlur, chain[0], half.Texture, half.Width, half.Height, srcScale: 0.5f,
                RegionAtLevel(padded, 0), RegionAtLevel(context.Bounds, 1));
            gl.Disable(EnableCap.ScissorTest);
            var blurred = chain[0];
            result = new GlBackdropResult(blurred.Texture, blurred.Width, blurred.Height, context.Bounds);
            return true;
        }

        for (var i = 1; i <= levels; i++)
        {
            var srcTexture = i == 1 ? context.Backdrop : chain[i - 1].Texture;
            var srcWidth = i == 1 ? context.TargetWidth : chain[i - 1].Width;
            var srcHeight = i == 1 ? context.TargetHeight : chain[i - 1].Height;
            var srcValid = i == 1 ? context.Bounds : RegionAtLevel(context.Bounds, i - 1);
            BlurPass(
                _down, chain[i], srcTexture, srcWidth, srcHeight, srcScale: 2f,
                RegionAtLevel(padded, i), srcValid);
        }

        for (var i = levels - 1; i >= 0; i--)
        {
            var src = chain[i + 1];
            BlurPass(
                i == 0 ? _onscreen : _up,
                chain[i], src.Texture, src.Width, src.Height, srcScale: 0.5f,
                RegionAtLevel(padded, i), RegionAtLevel(context.Bounds, i + 1));
        }

        gl.Disable(EnableCap.ScissorTest);
        var top = chain[0];
        result = new GlBackdropResult(top.Texture, top.Width, top.Height, context.Bounds);
        return true;
    }

    private void RecordSmol(in GlBackdropContext context, Pyramid pyramid)
    {
        var bounds = context.Bounds.Intersect(new Box(0, 0, context.TargetWidth, context.TargetHeight));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var sigma = Math.Max(0, SmolRadius()) / 3;
        var x = SmolAxis.For(sigma, bounds.Width);
        var y = SmolAxis.For(sigma, bounds.Height);
        var source = context.Backdrop;
        var physical = new Vector2(context.TargetWidth, context.TargetHeight);
        var origin = new Vector2(bounds.X, bounds.Y);
        var size = new Vector2(bounds.Width, bounds.Height);
        var pad = Vector2.Zero;
        var steps = new SmolSteps(x, y);
        while (steps.Next(out var levelX, out var levelY, out var fused))
        {
            var dst = SmolLevel(pyramid.Reduced, pyramid, levelX, levelY);
            var width = SmolAxis.SizeAt(bounds.Width, levelX);
            var height = SmolAxis.SizeAt(bounds.Height, levelY);
            var dstPad = new Vector2(levelX > 0 ? 1 : 0, levelY > 0 ? 1 : 0);
            var parameters = new SmolPassParameters
            {
                Source = new Vector4(origin, size.X, size.Y),
                SourcePad = new Vector4(pad, physical.X, physical.Y),
                Destination = new Vector4(width, height, dstPad.X, dstPad.Y),
            };
            SmolPass(
                fused ? _smolDown4 : _smolDown, dst, source,
                new Box(0, 0, width + (2 * (int)dstPad.X), height + (2 * (int)dstPad.Y)), parameters);
            source = dst.Texture;
            physical = new Vector2(dst.Width, dst.Height);
            origin = dstPad;
            size = new Vector2(width, height);
            pad = dstPad;
        }

        if (x.Level == 0 && y.Level == 0)
        {
            var clamp = new Vector4(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Right - 0.5f, bounds.Bottom - 0.5f);
            if (x.Filters)
            {
                var target = y.Filters ? SmolFull(ref pyramid.Filtered0, pyramid) : SmolFull(ref pyramid.Reconstructed, pyramid);
                SmolGauss(target, source, physical, horizontal: true, x, bounds, clamp);
                source = target.Texture;
            }

            if (y.Filters)
            {
                var target = SmolFull(ref pyramid.Reconstructed, pyramid);
                SmolGauss(target, source, physical, horizontal: false, y, bounds, clamp);
                source = target.Texture;
            }
        }
        else
        {
            var reduced = SmolLevel(pyramid.Reduced, pyramid, x.Level, y.Level);
            var other = SmolLevel(pyramid.Filtered, pyramid, x.Level, y.Level);
            var region = new Box(0, 0, x.Working + (2 * x.Pad), y.Working + (2 * y.Pad));
            var clamp = new Vector4(0.5f, 0.5f, region.Width - 0.5f, region.Height - 0.5f);
            var current = reduced;
            if (x.Filters)
            {
                SmolGauss(other, current.Texture, physical, horizontal: true, x, region, clamp);
                current = other;
            }

            if (y.Filters)
            {
                var target = current == reduced ? other : reduced;
                SmolGauss(target, current.Texture, physical, horizontal: false, y, region, clamp);
                current = target;
            }

            var reconstructed = SmolFull(ref pyramid.Reconstructed, pyramid);
            SmolPass(
                _smolReconstruct, reconstructed, current.Texture, bounds,
                new SmolPassParameters
                {
                    Source = new Vector4(x.Pad, y.Pad, x.Working, y.Working),
                    SourcePad = new Vector4(0, 0, physical.X, physical.Y),
                    Destination = new Vector4(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    Clamp = clamp,
                });
            source = reconstructed.Texture;
        }

        BlurPass(
            _onscreen, pyramid.Top, source, context.TargetWidth, context.TargetHeight,
            srcScale: 1f, bounds, bounds, plain: true);
    }

    private double SmolRadius() =>
        _current.Radius
        ?? (_current.Strength is { } strength ? BlurStrength.For(strength).Radius : _options.Radius ?? _strength.Radius);

    private void SmolGauss(Level dst, uint source, Vector2 physical, bool horizontal, in SmolAxis axis, in Box region, Vector4 clamp)
    {
        SmolPass(
            _smolGauss, dst, source, region,
            new SmolPassParameters
            {
                SourcePad = new Vector4(0, 0, physical.X, physical.Y),
                Axis = new Vector4(horizontal ? 1 : 0, horizontal ? 0 : 1, (float)axis.Sigma, axis.Taps),
                Clamp = clamp,
            });
    }

    private void SmolPass(Program program, Level dst, uint srcTexture, Box region, in SmolPassParameters parameters)
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
        Uniform(program.PcSource, parameters.Source);
        Uniform(program.PcSourcePad, parameters.SourcePad);
        Uniform(program.PcDestination, parameters.Destination);
        Uniform(program.PcAxis, parameters.Axis);
        Uniform(program.PcClamp, parameters.Clamp);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    private void Uniform(int location, Vector4 value)
    {
        if (location >= 0)
        {
            _device.Gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
        }
    }

    private Level SmolLevel(Dictionary<(int X, int Y), Level> levels, Pyramid pyramid, int levelX, int levelY)
    {
        if (!levels.TryGetValue((levelX, levelY), out var level))
        {
            level = CreateLevel(
                SmolAxis.SizeAt(pyramid.Width, levelX) + (levelX > 0 ? 2 : 0),
                SmolAxis.SizeAt(pyramid.Height, levelY) + (levelY > 0 ? 2 : 0),
                clear: true);
            levels[(levelX, levelY)] = level;
        }

        return level;
    }

    private Level SmolFull(ref Level? slot, Pyramid pyramid) => slot ??= CreateLevel(pyramid.Width, pyramid.Height, clear: true);

    private Level[] ChainOf(Pyramid pyramid)
    {
        if (pyramid.Chain is { } existing)
        {
            return existing;
        }

        var chain = new Level[_depth + 1];
        chain[0] = pyramid.Top;
        for (var i = 1; i <= _depth; i++)
        {
            chain[i] = CreateLevel(Math.Max(1, pyramid.Width >> i), Math.Max(1, pyramid.Height >> i));
        }

        pyramid.Chain = chain;
        return chain;
    }

    private static Box RegionAtLevel(in Box padded, int level)
    {
        var x = padded.X >> level;
        var y = padded.Y >> level;
        var right = ((padded.X + padded.Width) >> level) + 1;
        var bottom = ((padded.Y + padded.Height) >> level) + 1;
        return new Box(x, y, right - x, bottom - y);
    }

    private void BlurPass(
        Program program, Level dst, uint srcTexture, int srcWidth, int srcHeight, float srcScale, Box region,
        in Box srcValid, bool plain = false)
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
        var halfpixel = plain ? 0f : (float)(0.5 * _active.Offset);
        gl.Uniform2(program.Halfpixel, halfpixel / srcWidth, halfpixel / srcHeight);
        gl.Uniform4(
            program.SrcClamp,
            (srcValid.X + 0.5f) / srcWidth,
            (srcValid.Y + 0.5f) / srcHeight,
            (srcValid.Right - 0.5f) / srcWidth,
            (srcValid.Bottom - 0.5f) / srcHeight);
        if (program.Plain >= 0)
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

    private void TrackBackdrop(uint texture, int width, int height)
    {
        if (_backdrops.TryGetValue(texture, out var seen))
        {
            if (seen.Width == width && seen.Height == height)
            {
                return;
            }

            _backdrops[texture] = (width, height);
            DropUnusedPyramid(seen.Width, seen.Height);
            return;
        }

        _backdrops[texture] = (width, height);
    }

    private void ForgetTexture(uint texture)
    {
        if (_backdrops.Remove(texture, out var seen))
        {
            DropUnusedPyramid(seen.Width, seen.Height);
        }
    }

    private void DropUnusedPyramid(int width, int height)
    {
        foreach (var seen in _backdrops.Values)
        {
            if (seen.Width == width && seen.Height == height)
            {
                return;
            }
        }

        if (_pyramids.Remove((width, height), out var pyramid))
        {
            DestroyPyramid(pyramid);
        }
    }

    private Pyramid PyramidFor(int width, int height)
    {
        if (_pyramids.TryGetValue((width, height), out var existing))
        {
            return existing;
        }

        var pyramid = new Pyramid { Width = width, Height = height, Top = CreateLevel(width, height) };
        _pyramids[(width, height)] = pyramid;
        return pyramid;
    }

    private Level CreateLevel(int width, int height, bool clear = false)
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

        if (clear)
        {
            var scissor = gl.IsEnabled(EnableCap.ScissorTest);
            gl.Disable(EnableCap.ScissorTest);
            Span<float> zero = stackalloc float[4];
            gl.ClearBuffer(BufferKind.Color, 0, zero);
            if (scissor)
            {
                gl.Enable(EnableCap.ScissorTest);
            }
        }

        return level;
    }

    private Program Compile(string fragmentSource, string vertexSource = VertexSource)
    {
        var gl = _device.Gl;
        var handle = gl.CreateProgram();
        foreach (var (type, source) in new[] { (ShaderType.VertexShader, vertexSource), (ShaderType.FragmentShader, fragmentSource) })
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
            SrcClamp = gl.GetUniformLocation(handle, "u_srcClamp"),
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
            PcSource = gl.GetUniformLocation(handle, "pc.source"),
            PcSourcePad = gl.GetUniformLocation(handle, "pc.sourcePad"),
            PcDestination = gl.GetUniformLocation(handle, "pc.destination"),
            PcAxis = gl.GetUniformLocation(handle, "pc.axis"),
            PcClamp = gl.GetUniformLocation(handle, "pc.clampBox"),
        };
    }

    /// <summary>
    /// Deletes every program, pyramid and texture of the blur. The GL context must be current. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        _device.TextureDeleting -= ForgetTexture;
        _backdrops.Clear();
        var gl = _device.Gl;
        DropPyramids();
        gl.DeleteProgram(_down.Handle);
        gl.DeleteProgram(_up.Handle);
        gl.DeleteProgram(_onscreen.Handle);
        gl.DeleteProgram(_dwmDown.Handle);
        gl.DeleteProgram(_dwmBlur.Handle);
        gl.DeleteProgram(_smolDown.Handle);
        gl.DeleteProgram(_smolDown4.Handle);
        gl.DeleteProgram(_smolGauss.Handle);
        gl.DeleteProgram(_smolReconstruct.Handle);
        gl.DeleteProgram(_maskProgram);
        gl.DeleteProgram(_maskMeshProgram);
        if (_maskVao != 0)
        {
            gl.DeleteVertexArray(_maskVao);
            gl.DeleteBuffer(_maskVbo);
        }

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

    private void Widen(BlurStrength strength)
    {
        if (strength.Iterations > _widest.Iterations)
        {
            _widest = strength;
        }

        if (_widest.Iterations > _depth)
        {
            _depth = _widest.Iterations;
            DropPyramids();
        }
    }

    private void DropPyramids()
    {
        foreach (var pyramid in _pyramids.Values)
        {
            DestroyPyramid(pyramid);
        }

        _pyramids.Clear();
    }

    private void DestroyPyramid(Pyramid pyramid)
    {
        DestroyLevel(pyramid.Top);
        if (pyramid.Chain is { } chain)
        {
            for (var i = 1; i < chain.Length; i++)
            {
                DestroyLevel(chain[i]);
            }
        }

        foreach (var level in pyramid.Reduced.Values)
        {
            DestroyLevel(level);
        }

        foreach (var level in pyramid.Filtered.Values)
        {
            DestroyLevel(level);
        }

        if (pyramid.Filtered0 is { } filtered0)
        {
            DestroyLevel(filtered0);
        }

        if (pyramid.Reconstructed is { } reconstructed)
        {
            DestroyLevel(reconstructed);
        }

        if (pyramid.Masked is { } masked)
        {
            DestroyLevel(masked);
        }
    }

    private void DestroyLevel(Level level)
    {
        var gl = _device.Gl;
        _device.NotifyTextureDeleting(level.Texture);
        gl.DeleteFramebuffer(level.Fbo);
        gl.DeleteTexture(level.Texture);
    }
}
