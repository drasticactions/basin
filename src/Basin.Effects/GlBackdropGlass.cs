using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Silk.NET.OpenGLES;

namespace Basin.Effects;

/// <summary>
/// The glass backdrop effect on a GL device. It reads the shape of each key as a slab of glass over a blur.
/// </summary>
public sealed class GlBackdropGlass : IGlBackdropEffect, IBackdropGlass
{
    private const string VertexSource = """
        #version 300 es
        void main() {
            vec2 corner = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private sealed class Program
    {
        public uint Handle;
        public int Src;
        public int Plain;
        public int Box;
        public int Clamp;
        public int Hole;
        public int Highlight;
        public int Shape;
        public int Light;
        public int Tint;
        public int Extra;
    }

    private sealed class Target
    {
        public uint Texture;
        public uint Fbo;
        public int Width;
        public int Height;
    }

    private readonly GlDevice _device;
    private readonly GlBackdropBlur _blur;
    private readonly Program _composite;
    private readonly Program _fusedDown;
    private readonly Program _fused;
    private readonly Program _copy;
    private readonly Program _meshComposite;
    private readonly Program _meshFused;
    private uint _meshVao;
    private uint _meshVbo;
    private float[] _meshData = [];
    private readonly Dictionary<object, FrameBackdropShape> _shapes = [];
    private readonly Dictionary<(int Width, int Height), Target> _outputs = [];
    private readonly Dictionary<(int Width, int Height), Target> _reduced = [];
    private GlassOptions _options = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlBackdropGlass"/> class and compiles its programs on the device. It owns an inner <see cref="GlBackdropBlur"/>.
    /// </summary>
    /// <param name="device">The GL device. Its context must be current.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public GlBackdropGlass(GlDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _blur = new GlBackdropBlur(device);
        _composite = Compile(GlassShaders.GlHeader + GlassShaders.Composite);
        _fusedDown = Compile(GlassShaders.GlHeader + GlassShaders.FusedDown);
        _fused = Compile(GlassShaders.GlHeader + GlassShaders.Fused);
        _copy = Compile(GlassShaders.GlHeader + GlassShaders.Copy);
        _meshComposite = Compile(GlassShaders.GlHeader + GlassShaders.GlMeshInput + GlassShaders.MeshComposite, GlassShaders.GlMeshVertex);
        _meshFused = Compile(GlassShaders.GlHeader + GlassShaders.GlMeshInput + GlassShaders.MeshFused, GlassShaders.GlMeshVertex);
        Options = _options;
        BasinCounters.Track();
    }

    /// <inheritdoc/>
    public GlassOptions Options
    {
        get => _options;
        set
        {
            _options = value;
            _blur.Options = new BlurOptions
            {
                Strength = value.Strength,
                Kernel = value.Kernel,
                Saturation = 1.0,
                Contrast = 1.0,
                NoiseStrength = 0,
            };
        }
    }

    /// <inheritdoc/>
    public int ExpandSize => _blur.ExpandSize;

    internal int TargetCount => _outputs.Count + _reduced.Count;

    /// <summary>
    /// Sets the frame shape for the draws with a key. It replaces an earlier shape for the same key.
    /// </summary>
    /// <param name="key">The key that the draws use.</param>
    /// <param name="shape">The shape of the frame.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public void SetShape(object key, in FrameBackdropShape shape)
    {
        ArgumentNullException.ThrowIfNull(key);
        _shapes[key] = shape;
    }

    /// <summary>
    /// Drops the shape of one key.
    /// </summary>
    /// <param name="key">The key that <see cref="SetShape"/> took.</param>
    /// <returns><see langword="true"/> when the key had a shape.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool ForgetSurface(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _shapes.Remove(key);
    }

    /// <summary>
    /// Draws the glass for the bounds of the context into a texture of its own. With a mesh, the glass bends with the deformed node.
    /// </summary>
    /// <param name="context">The backdrop texture, the target size, the bounds, the extent, the key and the optional mesh.</param>
    /// <param name="result">The texture that holds the result and the box to composite from it.</param>
    /// <returns><see langword="false"/> when the bounds lie outside the target or the inner blur gives no result.</returns>
    public bool Record(in GlBackdropContext context, out GlBackdropResult result)
    {
        result = default;
        var bounds = context.Bounds.Intersect(new Box(0, 0, context.TargetWidth, context.TargetHeight));
        if (bounds.IsEmpty)
        {
            return false;
        }

        var shape = context.Key is { } key && _shapes.TryGetValue(key, out var stored) ? stored : default;
        var mesh = shape.Width > 0 && shape.Height > 0 ? context.Mesh.Span : default;
        var field = mesh.IsEmpty
            ? GlassFrame.For(_options, shape, context.Extent.IsEmpty ? context.Bounds : context.Extent, bounds, linear: false)
            : GlassFrame.ForLocal(_options, shape, bounds, linear: false);

        var gl = _device.Gl;
        var output = TargetFor(_outputs, context.TargetWidth, context.TargetHeight);
        if (_options.Pass == GlassPass.Fused)
        {
            var factor = GlassFrame.Factor(_options.Strength, out var sigma);
            var small = TargetFor(_reduced, (context.TargetWidth + factor - 1) / factor, (context.TargetHeight + factor - 1) / factor);
            var smallRegion = new Box(
                bounds.X / factor,
                bounds.Y / factor,
                ((bounds.Right + factor - 1) / factor) - (bounds.X / factor),
                ((bounds.Bottom + factor - 1) / factor) - (bounds.Y / factor));
            gl.Disable(EnableCap.Blend);
            gl.Enable(EnableCap.ScissorTest);
            Draw(_fusedDown, small, smallRegion, context.Backdrop, 0, field with { Extra = field.Extra with { Z = factor } });
            var fusedField = field with { Extra = field.Extra with { Z = factor, W = (float)sigma } };
            if (mesh.IsEmpty)
            {
                Draw(_fused, output, bounds, small.Texture, context.Backdrop, fusedField);
            }
            else
            {
                Draw(_copy, output, bounds, small.Texture, context.Backdrop, fusedField);
                DrawMesh(_meshFused, output, bounds, small.Texture, context.Backdrop, fusedField, mesh);
            }
        }
        else
        {
            if (!_blur.Record(context with { Mesh = default }, out var blurred) || blurred.Texture == 0)
            {
                return false;
            }

            gl.Disable(EnableCap.Blend);
            gl.Enable(EnableCap.ScissorTest);
            if (mesh.IsEmpty)
            {
                Draw(_composite, output, bounds, blurred.Texture, context.Backdrop, field);
            }
            else
            {
                Draw(_copy, output, bounds, blurred.Texture, context.Backdrop, field);
                DrawMesh(_meshComposite, output, bounds, blurred.Texture, context.Backdrop, field, mesh);
            }
        }

        gl.Disable(EnableCap.ScissorTest);
        result = new GlBackdropResult(output.Texture, output.Width, output.Height, bounds);
        return true;
    }

    private void Draw(Program program, Target target, in Box region, uint src, uint plain, in GlassFrame field)
    {
        var gl = _device.Gl;
        var width = Math.Min(region.Width, target.Width - region.X);
        var height = Math.Min(region.Height, target.Height - region.Y);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, target.Fbo);
        gl.Viewport(0, 0, (uint)target.Width, (uint)target.Height);
        gl.Scissor(region.X, region.Y, (uint)width, (uint)height);
        gl.UseProgram(program.Handle);
        Bind(TextureUnit.Texture0, src);
        gl.Uniform1(program.Src, 0);
        if (plain != 0)
        {
            Bind(TextureUnit.Texture1, plain);
            gl.Uniform1(program.Plain, 1);
            gl.ActiveTexture(TextureUnit.Texture0);
        }

        Uniform(program.Box, field.Box);
        Uniform(program.Clamp, field.Clamp);
        Uniform(program.Hole, field.Hole);
        Uniform(program.Highlight, field.Highlight);
        Uniform(program.Shape, field.Shape);
        Uniform(program.Light, field.Light);
        Uniform(program.Tint, field.Tint);
        Uniform(program.Extra, field.Extra);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    private void DrawMesh(
        Program program, Target target, in Box region, uint src, uint plain, in GlassFrame field, ReadOnlySpan<MeshVertex> mesh)
    {
        var gl = _device.Gl;
        if (_meshVao == 0)
        {
            _meshVao = gl.GenVertexArray();
            _meshVbo = gl.GenBuffer();
        }

        var floats = mesh.Length * 4;
        if (_meshData.Length < floats)
        {
            _meshData = new float[Math.Max(floats, _meshData.Length * 2)];
        }

        for (var i = 0; i < mesh.Length; i++)
        {
            var vertex = mesh[i];
            _meshData[(i * 4) + 0] = (vertex.X / target.Width * 2f) - 1f;
            _meshData[(i * 4) + 1] = (vertex.Y / target.Height * 2f) - 1f;
            _meshData[(i * 4) + 2] = vertex.U;
            _meshData[(i * 4) + 3] = vertex.V;
        }

        gl.BindVertexArray(_meshVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _meshVbo);
        unsafe
        {
            fixed (float* data = _meshData)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(floats * sizeof(float)), data, BufferUsageARB.StreamDraw);
            }

            gl.EnableVertexAttribArray(0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        }

        var width = Math.Min(region.Width, target.Width - region.X);
        var height = Math.Min(region.Height, target.Height - region.Y);
        if (width > 0 && height > 0)
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, target.Fbo);
            gl.Viewport(0, 0, (uint)target.Width, (uint)target.Height);
            gl.Scissor(region.X, region.Y, (uint)width, (uint)height);
            gl.UseProgram(program.Handle);
            Bind(TextureUnit.Texture0, src);
            gl.Uniform1(program.Src, 0);
            Bind(TextureUnit.Texture1, plain);
            gl.Uniform1(program.Plain, 1);
            gl.ActiveTexture(TextureUnit.Texture0);
            Uniform(program.Box, field.Box);
            Uniform(program.Clamp, field.Clamp);
            Uniform(program.Hole, field.Hole);
            Uniform(program.Highlight, field.Highlight);
            Uniform(program.Shape, field.Shape);
            Uniform(program.Light, field.Light);
            Uniform(program.Tint, field.Tint);
            Uniform(program.Extra, field.Extra);
            gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)mesh.Length);
        }

        gl.DisableVertexAttribArray(0);
        gl.DisableVertexAttribArray(1);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        gl.BindVertexArray(0);
    }

    private void Bind(TextureUnit unit, uint texture)
    {
        var gl = _device.Gl;
        gl.ActiveTexture(unit);
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    private void Uniform(int location, System.Numerics.Vector4 value)
    {
        if (location >= 0)
        {
            _device.Gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
        }
    }

    private Target TargetFor(Dictionary<(int Width, int Height), Target> cache, int width, int height)
    {
        if (cache.TryGetValue((width, height), out var existing))
        {
            return existing;
        }

        if (cache.Count >= 4)
        {
            DestroyTargets(cache);
        }

        var gl = _device.Gl;
        var target = new Target { Width = width, Height = height, Texture = gl.GenTexture() };
        gl.BindTexture(TextureTarget.Texture2D, target.Texture);
        gl.TexStorage2D(TextureTarget.Texture2D, 1, SizedInternalFormat.Rgba8, (uint)width, (uint)height);
        target.Fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, target.Fbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target.Texture, 0);
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException("glass framebuffer incomplete");
        }

        cache[(width, height)] = target;
        return target;
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
            Src = gl.GetUniformLocation(handle, "src"),
            Plain = gl.GetUniformLocation(handle, "plain"),
            Box = gl.GetUniformLocation(handle, "pc.box"),
            Clamp = gl.GetUniformLocation(handle, "pc.clampBox"),
            Hole = gl.GetUniformLocation(handle, "pc.hole"),
            Highlight = gl.GetUniformLocation(handle, "pc.highlight"),
            Shape = gl.GetUniformLocation(handle, "pc.shape"),
            Light = gl.GetUniformLocation(handle, "pc.light"),
            Tint = gl.GetUniformLocation(handle, "pc.tint"),
            Extra = gl.GetUniformLocation(handle, "pc.extra"),
        };
    }

    private void DestroyTargets(Dictionary<(int Width, int Height), Target> cache)
    {
        var gl = _device.Gl;
        foreach (var target in cache.Values)
        {
            _device.NotifyTextureDeleting(target.Texture);
            gl.DeleteFramebuffer(target.Fbo);
            gl.DeleteTexture(target.Texture);
        }

        cache.Clear();
    }

    /// <summary>
    /// Deletes every program and target of the glass and disposes the inner blur. The GL context must be current. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        DestroyTargets(_outputs);
        DestroyTargets(_reduced);
        var gl = _device.Gl;
        gl.DeleteProgram(_composite.Handle);
        gl.DeleteProgram(_fusedDown.Handle);
        gl.DeleteProgram(_fused.Handle);
        gl.DeleteProgram(_copy.Handle);
        gl.DeleteProgram(_meshComposite.Handle);
        gl.DeleteProgram(_meshFused.Handle);
        if (_meshVao != 0)
        {
            gl.DeleteVertexArray(_meshVao);
            gl.DeleteBuffer(_meshVbo);
        }

        _blur.Dispose();
    }
}
