using Silk.NET.OpenGLES;

namespace Basin.UI.Quill;

internal sealed unsafe class QuillGlProgram : IDisposable
{
    private const int VertexStride = 20;

    private readonly GL _gl;
    private int _vertexCapacity;
    private int _indexCapacity;
    private bool _disposed;

    internal QuillGlProgram(GL gl)
    {
        _gl = gl;
        Program = Link(gl, "canvas", QuillShaders.Vertex, QuillShaders.Fragment);

        Projection = gl.GetUniformLocation(Program, "projection");
        ViewportSize = gl.GetUniformLocation(Program, "viewportSize");
        ScissorTransform = gl.GetUniformLocation(Program, "scissorTransform");
        ScissorTranslation = gl.GetUniformLocation(Program, "scissorTranslation");
        ScissorExt = gl.GetUniformLocation(Program, "scissorExt");
        BrushTransform = gl.GetUniformLocation(Program, "brushTransform");
        BrushTranslation = gl.GetUniformLocation(Program, "brushTranslation");
        BrushType = gl.GetUniformLocation(Program, "brushType");
        BrushColor1 = gl.GetUniformLocation(Program, "brushColor1");
        BrushColor2 = gl.GetUniformLocation(Program, "brushColor2");
        BrushParams = gl.GetUniformLocation(Program, "brushParams");
        BrushParams2 = gl.GetUniformLocation(Program, "brushParams2");
        TextureTransform = gl.GetUniformLocation(Program, "textureTransform");
        TextureTranslation = gl.GetUniformLocation(Program, "textureTranslation");
        AtlasTexelSize = gl.GetUniformLocation(Program, "atlasTexelSize");
        SdfPxRange = gl.GetUniformLocation(Program, "sdfPxRange");
        BackdropBlurAmount = gl.GetUniformLocation(Program, "backdropBlurAmount");
        BackdropFlipY = gl.GetUniformLocation(Program, "backdropFlipY");
        BrushSampler = gl.GetUniformLocation(Program, "texture0");
        FontSampler = gl.GetUniformLocation(Program, "fontTexture");
        BackdropSampler = gl.GetUniformLocation(Program, "backdropTexture");

        Vao = gl.GenVertexArray();
        Vbo = gl.GenBuffer();
        Ebo = gl.GenBuffer();
        gl.BindVertexArray(Vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, Vbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, Ebo);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, VertexStride, (void*)0);
        gl.VertexAttribDivisor(0, 0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, VertexStride, (void*)8);
        gl.VertexAttribDivisor(1, 0);
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, VertexStride, (void*)16);
        gl.VertexAttribDivisor(2, 0);
        gl.BindVertexArray(0);

        White = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, White);
        var opaque = stackalloc byte[4] { 255, 255, 255, 255 };
        gl.TexImage2D(
            TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, opaque);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    internal uint Program { get; }

    internal uint Vao { get; }

    internal uint Vbo { get; }

    internal uint Ebo { get; }

    internal uint White { get; }

    internal int Projection { get; }

    internal int ViewportSize { get; }

    internal int ScissorTransform { get; }

    internal int ScissorTranslation { get; }

    internal int ScissorExt { get; }

    internal int BrushTransform { get; }

    internal int BrushTranslation { get; }

    internal int BrushType { get; }

    internal int BrushColor1 { get; }

    internal int BrushColor2 { get; }

    internal int BrushParams { get; }

    internal int BrushParams2 { get; }

    internal int TextureTransform { get; }

    internal int TextureTranslation { get; }

    internal int AtlasTexelSize { get; }

    internal int SdfPxRange { get; }

    internal int BackdropBlurAmount { get; }

    internal int BackdropFlipY { get; }

    internal int BrushSampler { get; }

    internal int FontSampler { get; }

    internal int BackdropSampler { get; }

    internal void UploadVertices(void* data, int bytes)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, Vbo);
        Stream(BufferTargetARB.ArrayBuffer, ref _vertexCapacity, data, bytes);
    }

    internal void UploadIndices(void* data, int bytes)
    {
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, Ebo);
        Stream(BufferTargetARB.ElementArrayBuffer, ref _indexCapacity, data, bytes);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gl.DeleteBuffer(Vbo);
        _gl.DeleteBuffer(Ebo);
        _gl.DeleteVertexArray(Vao);
        _gl.DeleteTexture(White);
        _gl.DeleteProgram(Program);
    }

    private void Stream(BufferTargetARB target, ref int capacity, void* data, int bytes)
    {
        if (bytes > capacity)
        {
            capacity = Math.Max(bytes, capacity == 0 ? 64 * 1024 : capacity * 2);
        }

        _gl.BufferData(target, (nuint)capacity, null, BufferUsageARB.StreamDraw);
        if (bytes > 0)
        {
            _gl.BufferSubData(target, 0, (nuint)bytes, data);
        }
    }

    private static uint Link(GL gl, string label, string vertexSource, string fragmentSource)
    {
        var program = gl.CreateProgram();
        foreach (var (type, source) in new[] { (ShaderType.VertexShader, vertexSource), (ShaderType.FragmentShader, fragmentSource) })
        {
            var shader = gl.CreateShader(type);
            gl.ShaderSource(shader, source);
            gl.CompileShader(shader);
            gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
            if (compiled == 0)
            {
                var log = gl.GetShaderInfoLog(shader);
                gl.DeleteShader(shader);
                gl.DeleteProgram(program);
                throw new InvalidOperationException($"Quill {label} {type}: {log}");
            }

            gl.AttachShader(program, shader);
            gl.DeleteShader(shader);
        }

        gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
        if (linked == 0)
        {
            var log = gl.GetProgramInfoLog(program);
            gl.DeleteProgram(program);
            throw new InvalidOperationException($"Quill {label} link: {log}");
        }

        return program;
    }
}
