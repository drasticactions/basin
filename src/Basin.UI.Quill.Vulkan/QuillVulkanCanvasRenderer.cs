using Basin.Render.Vulkan;
using Pixman;
using Prowl.Quill;
using Prowl.Vector;
using Silk.NET.Vulkan;

namespace Basin.UI.Quill;

public sealed unsafe class QuillVulkanCanvasRenderer : ICanvasRenderer
{
    private readonly VulkanDevice _device;
    private readonly QuillVulkanPipeline _pipeline;
    private readonly QuillVulkanTextures _textures;
    private CommandBuffer _commands;
    private QuillVulkanFrame? _frame;
    private Framebuffer _framebuffer;
    private int _width = 1;
    private int _height = 1;
    private int _passes;
    private bool _disposed;

    internal QuillVulkanCanvasRenderer(VulkanDevice device, QuillVulkanPipeline pipeline, QuillVulkanTextures textures)
    {
        _device = device;
        _pipeline = pipeline;
        _textures = textures;
    }

    public bool SupportsBackdropBlur => false;

    internal int Passes => _passes;

    internal void Open(CommandBuffer commands, QuillVulkanFrame frame, Framebuffer framebuffer, int width, int height)
    {
        _commands = commands;
        _frame = frame;
        _framebuffer = framebuffer;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _passes = 0;
    }

    internal void Close()
    {
        _commands = default;
        _frame = null;
        _framebuffer = default;
    }

    public object CreateTexture(uint width, uint height) => _textures.Create((int)width, (int)height);

    public Int2 GetTextureSize(object texture) => texture is QuillTexture quill
        ? new Int2(quill.Width, quill.Height)
        : new Int2(0, 0);

    public void SetTextureData(object texture, IntRect bounds, byte[] data)
    {
        if (texture is not QuillVulkanTexture quill || data is null)
        {
            return;
        }

        var box = new Box(bounds.Min.X, bounds.Min.Y, bounds.Max.X - bounds.Min.X, bounds.Max.Y - bounds.Min.Y);
        _textures.Update(quill, box, data);
    }

    public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
    {
        if (_disposed || _frame is null || canvas is null || drawCalls is null)
        {
            return;
        }

        Record(canvas, drawCalls);
    }

    internal void RecordClear()
    {
        if (_frame is null)
        {
            return;
        }

        Record(null, []);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close();
    }

    private void Record(Canvas? canvas, IReadOnlyList<DrawCall> drawCalls)
    {
        var vk = _device.Api;
        var frame = _frame!;
        var commands = _commands;
        _textures.Record(commands);

        var draws = canvas is null ? 0 : drawCalls.Count;
        QuillVulkanBuffer? vertices = null;
        QuillVulkanBuffer? indices = null;
        ulong vertexOffset = 0;
        ulong indexOffset = 0;
        if (draws > 0)
        {
            var vertexBytes = canvas!.VertexCount * Vertex.SizeInBytes;
            var indexBytes = canvas.IndexCount * sizeof(uint);
            if (vertexBytes == 0 || indexBytes == 0)
            {
                draws = 0;
            }
            else
            {
                vertices = frame.Vertices(vertexBytes, out vertexOffset);
                indices = frame.Indices(indexBytes, out indexOffset);
                fixed (Vertex* source = canvas.VertexBuffer)
                {
                    System.Buffer.MemoryCopy(source, vertices.Mapped + vertexOffset, vertexBytes, vertexBytes);
                }

                fixed (uint* source = canvas.IndexBuffer)
                {
                    System.Buffer.MemoryCopy(source, indices.Mapped + indexOffset, indexBytes, indexBytes);
                }
            }
        }

        var clear = default(ClearValue);
        var passBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _passes == 0 ? _pipeline.ClearPass : _pipeline.LoadPass,
            Framebuffer = _framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)_width, (uint)_height)),
            ClearValueCount = 1,
            PClearValues = &clear,
        };
        _passes++;

        if (draws == 0)
        {
            vk.CmdBeginRenderPass(commands, in passBegin, SubpassContents.Inline);
            vk.CmdEndRenderPass(commands);
            return;
        }

        var uniforms = frame.Uniforms(draws, out var uniformBase);
        var stride = _pipeline.UniformStride;
        var scale = canvas!.FramebufferScale;
        var sdfPxRange = canvas.Text.FontEngine.DistanceRange;
        var white = _textures.White;
        var sets = stackalloc DescriptorSet[draws];
        for (var i = 0; i < draws; i++)
        {
            var call = drawCalls[i];
            var atlas = call.FontAtlas as QuillVulkanTexture;
            var brushTexture = call.Texture as QuillVulkanTexture;
            var block = (QuillVulkanUniforms*)(uniforms.Mapped + uniformBase + ((ulong)i * stride));
            Fill(block, call, scale, sdfPxRange, atlas);
            sets[i] = frame.AllocateSet();
            Write(sets[i], uniforms, brushTexture ?? white, atlas ?? white, white);
        }

        vk.CmdBeginRenderPass(commands, in passBegin, SubpassContents.Inline);
        vk.CmdBindPipeline(commands, PipelineBindPoint.Graphics, _pipeline.Pipeline);
        var viewport = new Viewport(0, 0, _width, _height, 0, 1);
        vk.CmdSetViewport(commands, 0, 1, in viewport);
        var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)_width, (uint)_height));
        vk.CmdSetScissor(commands, 0, 1, in scissor);
        var vertexBuffer = vertices!.Handle;
        vk.CmdBindVertexBuffers(commands, 0, 1, &vertexBuffer, &vertexOffset);
        vk.CmdBindIndexBuffer(commands, indices!.Handle, indexOffset, IndexType.Uint32);

        uint first = 0;
        for (var i = 0; i < draws; i++)
        {
            var count = (uint)drawCalls[i].ElementCount;
            if (count > 0)
            {
                var dynamicOffset = (uint)(uniformBase + ((ulong)i * stride));
                var set = sets[i];
                vk.CmdBindDescriptorSets(
                    commands, PipelineBindPoint.Graphics, _pipeline.Layout, 0, 1, &set, 1, &dynamicOffset);
                vk.CmdDrawIndexed(commands, count, 1, first, 0, 0);
            }

            first += count;
        }

        vk.CmdEndRenderPass(commands);
    }

    private void Fill(QuillVulkanUniforms* block, DrawCall call, float scale, float sdfPxRange, QuillVulkanTexture? atlas)
    {
        *block = default;
        block->Projection[0] = 2f / _width;
        block->Projection[3] = -1f;
        block->Projection[5] = 2f / _height;
        block->Projection[7] = -1f;
        block->Projection[10] = 1f;
        block->Projection[15] = 1f;

        call.GetScissor(scale, out var scissor, out var scissorTranslation, out var scissorExtent);
        block->ScissorTransform[0] = scissor.X;
        block->ScissorTransform[1] = scissor.Y;
        block->ScissorTransform[2] = scissor.Z;
        block->ScissorTransform[3] = scissor.W;
        block->ScissorTranslation[0] = scissorTranslation.X;
        block->ScissorTranslation[1] = scissorTranslation.Y;
        block->ScissorExt[0] = scissorExtent.X;
        block->ScissorExt[1] = scissorExtent.Y;

        call.GetBrushTransform(scale, out var brush, out var brushTranslation);
        block->BrushTransform[0] = brush.X;
        block->BrushTransform[1] = brush.Y;
        block->BrushTransform[2] = brush.Z;
        block->BrushTransform[3] = brush.W;
        block->BrushTranslation[0] = brushTranslation.X;
        block->BrushTranslation[1] = brushTranslation.Y;
        block->BrushType = (int)call.Brush.Type;
        SetColor(block->BrushColor1, call.Brush.Color1);
        SetColor(block->BrushColor2, call.Brush.Color2);
        block->BrushParams[0] = call.Brush.Point1.X;
        block->BrushParams[1] = call.Brush.Point1.Y;
        block->BrushParams[2] = call.Brush.Point2.X;
        block->BrushParams[3] = call.Brush.Point2.Y;
        block->BrushParams2[0] = call.Brush.CornerRadii;
        block->BrushParams2[1] = call.Brush.Feather;

        call.GetTextureTransform(scale, out var texture, out var textureTranslation);
        block->TextureTransform[0] = texture.X;
        block->TextureTransform[1] = texture.Y;
        block->TextureTransform[2] = texture.Z;
        block->TextureTransform[3] = texture.W;
        block->TextureTranslation[0] = textureTranslation.X;
        block->TextureTranslation[1] = textureTranslation.Y;

        block->AtlasTexelSize[0] = atlas is null || atlas.Width <= 0 ? 0f : 1f / atlas.Width;
        block->AtlasTexelSize[1] = atlas is null || atlas.Height <= 0 ? 0f : 1f / atlas.Height;
        block->SdfPxRange = sdfPxRange;
        block->ViewportSize[0] = _width;
        block->ViewportSize[1] = _height;
        block->BackdropBlurAmount = 0f;
        block->BackdropFlipY = 0;
    }

    private void Write(
        DescriptorSet set, QuillVulkanBuffer uniforms, QuillVulkanTexture brush, QuillVulkanTexture atlas,
        QuillVulkanTexture backdrop)
    {
        var bufferInfo = new DescriptorBufferInfo(uniforms.Handle, 0, QuillVulkanPipeline.UniformSize);
        var images = stackalloc DescriptorImageInfo[3];
        images[0] = new DescriptorImageInfo(default, brush.View, ImageLayout.ShaderReadOnlyOptimal);
        images[1] = new DescriptorImageInfo(default, atlas.View, ImageLayout.ShaderReadOnlyOptimal);
        images[2] = new DescriptorImageInfo(default, backdrop.View, ImageLayout.ShaderReadOnlyOptimal);
        var writes = stackalloc WriteDescriptorSet[4];
        writes[0] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBufferDynamic,
            PBufferInfo = &bufferInfo,
        };
        for (var i = 0; i < 3; i++)
        {
            writes[i + 1] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = (uint)(i + 1),
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = images + i,
            };
        }

        _device.Api.UpdateDescriptorSets(_device.Device, 4, writes, 0, null);
    }

    private static void SetColor(float* into, Color32 color)
    {
        into[0] = color.R / 255f;
        into[1] = color.G / 255f;
        into[2] = color.B / 255f;
        into[3] = color.A / 255f;
    }
}
