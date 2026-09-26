using Pixman;
using Prowl.Quill;
using Prowl.Vector;
using Silk.NET.OpenGLES;

namespace Basin.UI.Quill;

public sealed unsafe class QuillCanvasRenderer : ICanvasRenderer
{
    private readonly GL _gl;
    private readonly QuillGlProgram _program;
    private readonly QuillTextures _textures;
    private readonly List<QuillGlTexture> _created = [];
    private uint _framebuffer;
    private int _width = 1;
    private int _height = 1;
    private bool _disposed;

    internal QuillCanvasRenderer(GL gl, QuillGlProgram program, QuillTextures textures)
    {
        _gl = gl;
        _program = program;
        _textures = textures;
    }

    public bool SupportsBackdropBlur => false;

    internal void SetTarget(uint framebuffer, int width, int height)
    {
        _framebuffer = framebuffer;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
    }

    public object CreateTexture(uint width, uint height)
    {
        var texture = _textures.Create((int)width, (int)height);
        _created.Add(texture);
        return texture;
    }

    public Int2 GetTextureSize(object texture) => texture is QuillTexture quill
        ? new Int2(quill.Width, quill.Height)
        : new Int2(0, 0);

    public void SetTextureData(object texture, IntRect bounds, byte[] data)
    {
        if (texture is not QuillGlTexture quill || data is null)
        {
            return;
        }

        var box = new Box(bounds.Min.X, bounds.Min.Y, bounds.Max.X - bounds.Min.X, bounds.Max.Y - bounds.Min.Y);
        _textures.Update(quill, box, data);
    }

    public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
    {
        if (_disposed || canvas is null || drawCalls is null || drawCalls.Count == 0)
        {
            return;
        }

        var gl = _gl;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.Viewport(0, 0, (uint)_width, (uint)_height);
        gl.Disable(EnableCap.ScissorTest);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.StencilTest);
        gl.Disable(EnableCap.CullFace);
        gl.ColorMask(true, true, true, true);
        gl.Enable(EnableCap.Blend);
        gl.BlendEquation(GLEnum.FuncAdd);
        gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        gl.UseProgram(_program.Program);
        gl.BindVertexArray(_program.Vao);

        fixed (Vertex* vertices = canvas.VertexBuffer)
        {
            _program.UploadVertices(vertices, canvas.VertexCount * Vertex.SizeInBytes);
        }

        fixed (uint* indices = canvas.IndexBuffer)
        {
            _program.UploadIndices(indices, canvas.IndexCount * sizeof(uint));
        }

        Span<float> projection = stackalloc float[16];
        projection.Clear();
        projection[0] = 2f / _width;
        projection[3] = -1f;
        projection[5] = 2f / _height;
        projection[7] = -1f;
        projection[10] = 1f;
        projection[15] = 1f;
        gl.UniformMatrix4(_program.Projection, 1, false, projection);
        gl.Uniform2(_program.ViewportSize, (float)_width, (float)_height);
        gl.Uniform1(_program.BrushSampler, 0);
        gl.Uniform1(_program.FontSampler, 1);
        gl.Uniform1(_program.BackdropSampler, 3);
        gl.Uniform1(_program.BackdropBlurAmount, 0f);
        gl.Uniform1(_program.BackdropFlipY, 0);
        gl.Uniform1(_program.SdfPxRange, canvas.Text.FontEngine.DistanceRange);
        gl.ActiveTexture(TextureUnit.Texture3);
        gl.BindTexture(TextureTarget.Texture2D, _program.White);

        var scale = canvas.FramebufferScale;
        var offset = 0;
        for (var i = 0; i < drawCalls.Count; i++)
        {
            var call = drawCalls[i];
            var atlas = call.FontAtlas as QuillGlTexture;
            gl.ActiveTexture(TextureUnit.Texture1);
            gl.BindTexture(TextureTarget.Texture2D, atlas is null ? _program.White : atlas.Name);
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindTexture(
                TextureTarget.Texture2D,
                call.Texture is QuillGlTexture brushTexture ? brushTexture.Name : _program.White);

            call.GetScissor(scale, out var scissor, out var scissorTranslation, out var scissorExtent);
            gl.Uniform4(_program.ScissorTransform, scissor.X, scissor.Y, scissor.Z, scissor.W);
            gl.Uniform2(_program.ScissorTranslation, scissorTranslation.X, scissorTranslation.Y);
            gl.Uniform2(_program.ScissorExt, scissorExtent.X, scissorExtent.Y);

            call.GetBrushTransform(scale, out var brush, out var brushTranslation);
            gl.Uniform4(_program.BrushTransform, brush.X, brush.Y, brush.Z, brush.W);
            gl.Uniform2(_program.BrushTranslation, brushTranslation.X, brushTranslation.Y);
            gl.Uniform1(_program.BrushType, (int)call.Brush.Type);
            SetColor(gl, _program.BrushColor1, call.Brush.Color1);
            SetColor(gl, _program.BrushColor2, call.Brush.Color2);
            gl.Uniform4(
                _program.BrushParams,
                call.Brush.Point1.X, call.Brush.Point1.Y, call.Brush.Point2.X, call.Brush.Point2.Y);
            gl.Uniform2(_program.BrushParams2, call.Brush.CornerRadii, call.Brush.Feather);

            call.GetTextureTransform(scale, out var texture, out var textureTranslation);
            gl.Uniform4(_program.TextureTransform, texture.X, texture.Y, texture.Z, texture.W);
            gl.Uniform2(_program.TextureTranslation, textureTranslation.X, textureTranslation.Y);

            var atlasWidth = atlas is null ? 0 : atlas.Width;
            var atlasHeight = atlas is null ? 0 : atlas.Height;
            gl.Uniform2(
                _program.AtlasTexelSize,
                atlasWidth > 0 ? 1f / atlasWidth : 0f,
                atlasHeight > 0 ? 1f / atlasHeight : 0f);

            gl.DrawElements(
                PrimitiveType.Triangles, (uint)call.ElementCount, DrawElementsType.UnsignedInt,
                (void*)(nint)(offset * sizeof(uint)));
            offset += call.ElementCount;
        }

        gl.BindVertexArray(0);
        gl.ActiveTexture(TextureUnit.Texture3);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var texture in _created)
        {
            _textures.Destroy(texture);
        }

        _created.Clear();
    }

    private static void SetColor(GL gl, int location, Color32 color) =>
        gl.Uniform4(location, color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
}
