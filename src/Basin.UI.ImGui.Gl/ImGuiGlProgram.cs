using Silk.NET.OpenGLES;

namespace Basin.UI.ImGui;

internal sealed unsafe class ImGuiGlProgram : IDisposable
{
    private const int VertexStride = 20;

    private readonly GL _gl;
    private int _vertexCapacity;
    private int _indexCapacity;
    private bool _disposed;

    internal ImGuiGlProgram(GL gl)
    {
        _gl = gl;
        Program = Link(gl, ImGuiGlShaders.Vertex, ImGuiGlShaders.Fragment);
        Projection = gl.GetUniformLocation(Program, "ProjMtx");
        Sampler = gl.GetUniformLocation(Program, "Texture");

        Vao = gl.GenVertexArray();
        Vbo = gl.GenBuffer();
        Ibo = gl.GenBuffer();
        gl.BindVertexArray(Vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, Vbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, Ibo);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, VertexStride, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, VertexStride, (void*)8);
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, VertexStride, (void*)16);
        gl.BindVertexArray(0);

        var version = gl.GetStringS(StringName.Version) ?? string.Empty;
        HasBaseVertex = EsMinor(version) >= 2;
    }

    internal uint Program { get; }

    internal uint Vao { get; }

    internal uint Vbo { get; }

    internal uint Ibo { get; }

    internal int Projection { get; }

    internal int Sampler { get; }

    internal bool HasBaseVertex { get; }

    internal void Upload(void* vertices, int vertexBytes, void* indices, int indexBytes)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, Vbo);
        Stream(BufferTargetARB.ArrayBuffer, ref _vertexCapacity, vertices, vertexBytes);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, Ibo);
        Stream(BufferTargetARB.ElementArrayBuffer, ref _indexCapacity, indices, indexBytes);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gl.DeleteBuffer(Vbo);
        _gl.DeleteBuffer(Ibo);
        _gl.DeleteVertexArray(Vao);
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

    private static int EsMinor(string version)
    {
        var at = version.IndexOf("ES ", StringComparison.Ordinal);
        if (at < 0)
        {
            return 0;
        }

        var numbers = version.AsSpan(at + 3);
        var dot = numbers.IndexOf('.');
        if (dot < 0 || !int.TryParse(numbers[..dot], out var major))
        {
            return 0;
        }

        var rest = numbers[(dot + 1)..];
        var end = 0;
        while (end < rest.Length && char.IsAsciiDigit(rest[end]))
        {
            end++;
        }

        return major > 3 ? 99 : major == 3 && int.TryParse(rest[..end], out var minor) ? minor : 0;
    }

    private static uint Link(GL gl, string vertexSource, string fragmentSource)
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
                throw new InvalidOperationException($"The ImGui {type} shader did not compile. {log.Trim()}");
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
            throw new InvalidOperationException($"The ImGui program did not link. {log.Trim()}");
        }

        return program;
    }
}
