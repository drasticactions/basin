using Basin.Render.Vulkan;
using Basin.Scene;
using Silk.NET.Vulkan;
using Xunit;

namespace Basin.Tests;

public sealed class BackdropEffectSeamTests
{
    [Fact]
    public void Vulkan_composites_the_effect_result_into_the_region()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var renderer = new VulkanRenderer(CompositorTestHost.RenderNodePath);
        Assert.True(((IRenderer)renderer).SupportsBackdropEffects);

        var scene = new Scene.Scene();
        var content = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        Fill(content, 0x00000000);
        var node = new SceneBuffer(scene.Root);
        node.SetPosition(16, 16);
        node.SetBuffer(content);

        using var region = new Pixman.PixmanRegion32(0, 0, 16, 16);
        using var effect = new PaintGreenEffect(renderer.Device);
        node.SetBackdropEffect(effect, region);

        var target = new MemoryBuffer(64, 64, DrmFormat.Xrgb8888);
        Assert.True(scene.Render(renderer, target, new RenderColor(1, 0, 0, 1)));

        unsafe
        {
            Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
            var inRegion = *(uint*)(view.Data + 20 * view.Stride + 20 * 4);
            var inNode = *(uint*)(view.Data + 40 * view.Stride + 40 * 4);
            var outside = *(uint*)(view.Data + 8 * view.Stride + 8 * 4);
            target.EndDataAccess();
            Assert.Equal(0xFF00FF00u, inRegion);
            Assert.Equal(0xFFFF0000u, inNode);
            Assert.Equal(0xFFFF0000u, outside);
        }

        region.Clear();
        node.SetBackdropEffect(effect, region);
        Assert.True(scene.Render(renderer, target, new RenderColor(1, 0, 0, 1)));
        unsafe
        {
            Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
            var inRegion = *(uint*)(view.Data + 20 * view.Stride + 20 * 4);
            target.EndDataAccess();
            Assert.Equal(0xFFFF0000u, inRegion);
        }

        node.Destroy();
        target.Destroy();
        content.Destroy();
    }

    [Fact]
    public void A_whole_surface_region_survives_a_scaled_output()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var renderer = new VulkanRenderer(CompositorTestHost.RenderNodePath);

        var scene = new Scene.Scene();
        var content = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        Fill(content, 0x00000000);
        var node = new SceneBuffer(scene.Root);
        node.SetPosition(16, 16);
        node.SetBuffer(content);

        using var region = new Pixman.PixmanRegion32(-1073741824, -1073741824, 2147483647, 2147483647);
        using var effect = new PaintGreenEffect(renderer.Device);
        node.SetBackdropEffect(effect, region);

        var target = new MemoryBuffer(128, 128, DrmFormat.Xrgb8888);
        Assert.True(scene.Render(renderer, target, new RenderColor(1, 0, 0, 1), 2.0));

        unsafe
        {
            Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
            var inNode = *(uint*)(view.Data + 40 * view.Stride + 40 * 4);
            var outside = *(uint*)(view.Data + 8 * view.Stride + 8 * 4);
            target.EndDataAccess();
            Assert.Equal(0xFF00FF00u, inNode);
            Assert.Equal(0xFFFF0000u, outside);
        }

        node.Destroy();
        target.Destroy();
        content.Destroy();
    }

    [Fact]
    public void A_backdrop_under_a_matrix_transform_moves_with_the_content()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var renderer = new VulkanRenderer(CompositorTestHost.RenderNodePath);

        var scene = new Scene.Scene();
        var transform = new SceneTransform(scene.Root) { Matrix = RenderTransform.Translation(20, 0) };
        var content = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        Fill(content, 0x00000000);
        var node = new SceneBuffer(transform);
        node.SetPosition(16, 16);
        node.SetBuffer(content);

        using var region = new Pixman.PixmanRegion32(0, 0, 32, 32);
        using var effect = new PaintGreenEffect(renderer.Device);
        node.SetBackdropEffect(effect, region);

        var target = new MemoryBuffer(96, 64, DrmFormat.Xrgb8888);
        Assert.True(scene.Render(renderer, target, new RenderColor(1, 0, 0, 1)));

        unsafe
        {
            Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
            var moved = *(uint*)(view.Data + 32 * view.Stride + 50 * 4);
            var vacated = *(uint*)(view.Data + 32 * view.Stride + 18 * 4);
            target.EndDataAccess();
            Assert.Equal(0xFF00FF00u, moved);
            Assert.Equal(0xFFFF0000u, vacated);
        }

        transform.Destroy();
        target.Destroy();
        content.Destroy();
    }

    [Fact]
    public void A_backdrop_under_a_deformer_follows_the_mesh()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var renderer = new VulkanRenderer(CompositorTestHost.RenderNodePath);

        var scene = new Scene.Scene();
        var transform = new SceneTransform(scene.Root) { Deformer = new ShiftDeformer(20) };
        var content = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        Fill(content, 0x00000000);
        var node = new SceneBuffer(transform);
        node.SetPosition(16, 16);
        node.SetBuffer(content);

        using var region = new Pixman.PixmanRegion32(0, 0, 32, 32);
        using var effect = new PaintGreenEffect(renderer.Device);
        node.SetBackdropEffect(effect, region);

        var target = new MemoryBuffer(96, 64, DrmFormat.Xrgb8888);
        Assert.True(scene.Render(renderer, target, new RenderColor(1, 0, 0, 1)));

        unsafe
        {
            Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
            var moved = *(uint*)(view.Data + 32 * view.Stride + 50 * 4);
            var vacated = *(uint*)(view.Data + 32 * view.Stride + 18 * 4);
            target.EndDataAccess();
            Assert.Equal(0xFF00FF00u, moved);
            Assert.Equal(0xFFFF0000u, vacated);
        }

        transform.Destroy();
        target.Destroy();
        content.Destroy();
    }

    [Fact]
    public void A_captured_deformed_body_stays_translucent_over_its_backdrop()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var renderer = new VulkanRenderer(CompositorTestHost.RenderNodePath);

        var scene = new Scene.Scene();
        var transform = new SceneTransform(scene.Root) { Deformer = new ShiftDeformer(20) };
        var content = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        Fill(content, 0x80000000);
        var node = new SceneBuffer(transform);
        node.SetPosition(16, 16);
        node.SetBuffer(content);
        var sibling = new SceneRect(transform, 4, 4, new RenderColor(0f, 0f, 1f, 1f));
        sibling.SetPosition(16, 16);

        using var region = new Pixman.PixmanRegion32(0, 0, 32, 32);
        using var effect = new PaintGreenEffect(renderer.Device);
        node.SetBackdropEffect(effect, region);

        var target = new MemoryBuffer(96, 64, DrmFormat.Xrgb8888);
        Assert.True(scene.Render(renderer, target, new RenderColor(1, 0, 0, 1)));

        unsafe
        {
            Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
            var moved = *(uint*)(view.Data + 32 * view.Stride + 50 * 4);
            target.EndDataAccess();
            var red = (moved >> 16) & 0xFF;
            var green = (moved >> 8) & 0xFF;
            Assert.True(green > 100, $"the backdrop shows through the translucent body: {moved:X8}");
            Assert.True(red < 40, $"the body is not opaque over the old background: {moved:X8}");
        }

        transform.Destroy();
        target.Destroy();
        content.Destroy();
    }

    private sealed class ShiftDeformer : IMeshTransform
    {
        private readonly int _shift;

        public ShiftDeformer(int shift)
        {
            _shift = shift;
        }

        public Box MapBounds(in Box childBounds) =>
            new(childBounds.X + _shift, childBounds.Y, childBounds.Width, childBounds.Height);

        public int VertexCount(in Box childBounds) => 6;

        public void WriteVertices(in Box childBounds, Span<MeshVertex> into)
        {
            var white = new RenderColor(1f, 1f, 1f, 1f);
            float left = childBounds.X, top = childBounds.Y, right = childBounds.Right, bottom = childBounds.Bottom;
            into[0] = new MeshVertex(left + _shift, top, left, top, white);
            into[1] = new MeshVertex(right + _shift, top, right, top, white);
            into[2] = new MeshVertex(left + _shift, bottom, left, bottom, white);
            into[3] = new MeshVertex(right + _shift, top, right, top, white);
            into[4] = new MeshVertex(right + _shift, bottom, right, bottom, white);
            into[5] = new MeshVertex(left + _shift, bottom, left, bottom, white);
        }
    }

    [Fact]
    public void A_foreign_effect_is_rejected_like_a_foreign_texture()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var renderer = new VulkanRenderer(CompositorTestHost.RenderNodePath);
        var target = new MemoryBuffer(16, 16, DrmFormat.Xrgb8888);
        var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
        Assert.Throws<ArgumentException>(() => pass.AddBackdropEffect(new NotAVulkanEffect(), new Box(0, 0, 8, 8)));
        Assert.True(pass.Submit());
        target.Destroy();
    }


    [Fact]
    public void Gl_composites_the_effect_result_into_the_region()
    {
        CompositorTestHost.SkipUnlessRunnable("gl");
        using var renderer = new Basin.Render.Gl.GlRenderer(CompositorTestHost.RenderNodePath);
        Assert.True(((IRenderer)renderer).SupportsBackdropEffects);

        var scene = new Scene.Scene();
        var content = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        Fill(content, 0x00000000);
        var node = new SceneBuffer(scene.Root);
        node.SetPosition(16, 16);
        node.SetBuffer(content);

        using var region = new Pixman.PixmanRegion32(0, 0, 16, 16);
        using var effect = new PaintGreenGlEffect(renderer.Device);
        node.SetBackdropEffect(effect, region);

        var target = new MemoryBuffer(64, 64, DrmFormat.Xrgb8888);
        Assert.True(scene.Render(renderer, target, new RenderColor(1, 0, 0, 1)));

        unsafe
        {
            Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
            var inRegion = *(uint*)(view.Data + 20 * view.Stride + 20 * 4);
            var inNode = *(uint*)(view.Data + 40 * view.Stride + 40 * 4);
            var outside = *(uint*)(view.Data + 8 * view.Stride + 8 * 4);
            target.EndDataAccess();
            Assert.Equal(0xFF00FF00u, inRegion);
            Assert.Equal(0xFFFF0000u, inNode);
            Assert.Equal(0xFFFF0000u, outside);
        }

        region.Clear();
        node.SetBackdropEffect(effect, region);
        Assert.True(scene.Render(renderer, target, new RenderColor(1, 0, 0, 1)));
        unsafe
        {
            Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var view));
            var inRegion = *(uint*)(view.Data + 20 * view.Stride + 20 * 4);
            target.EndDataAccess();
            Assert.Equal(0xFFFF0000u, inRegion);
        }

        node.Destroy();
        target.Destroy();
        content.Destroy();
    }

    [Fact]
    public void Gl_rejects_a_foreign_effect()
    {
        CompositorTestHost.SkipUnlessRunnable("gl");
        using var host = new CompositorTestHost(renderer: "gl");
        var target = new MemoryBuffer(32, 32, DrmFormat.Xrgb8888);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        Assert.Throws<ArgumentException>(() => pass.AddBackdropEffect(new ForeignEffect(), new Box(0, 0, 16, 16)));
        Assert.True(pass.Submit());
        target.Destroy();
    }

    private sealed class ForeignEffect : IBackdropEffect
    {
    }

    private sealed class PaintGreenGlEffect(Basin.Render.Gl.GlDevice device) : Basin.Render.Gl.IGlBackdropEffect, IDisposable
    {
        private uint _texture;
        private uint _fbo;
        private int _width;
        private int _height;

        public bool Record(in Basin.Render.Gl.GlBackdropContext context, out Basin.Render.Gl.GlBackdropResult result)
        {
            var gl = device.Gl;
            var bounds = context.Bounds;
            if (_texture == 0 || _width != bounds.Width || _height != bounds.Height)
            {
                DestroyTexture();
                _texture = gl.GenTexture();
                gl.BindTexture(Silk.NET.OpenGLES.TextureTarget.Texture2D, _texture);
                gl.TexStorage2D(Silk.NET.OpenGLES.TextureTarget.Texture2D, 1, Silk.NET.OpenGLES.SizedInternalFormat.Rgba8, (uint)bounds.Width, (uint)bounds.Height);
                _fbo = gl.GenFramebuffer();
                gl.BindFramebuffer(Silk.NET.OpenGLES.FramebufferTarget.Framebuffer, _fbo);
                gl.FramebufferTexture2D(Silk.NET.OpenGLES.FramebufferTarget.Framebuffer, Silk.NET.OpenGLES.FramebufferAttachment.ColorAttachment0, Silk.NET.OpenGLES.TextureTarget.Texture2D, _texture, 0);
                _width = bounds.Width;
                _height = bounds.Height;
            }

            gl.BindFramebuffer(Silk.NET.OpenGLES.FramebufferTarget.Framebuffer, _fbo);
            gl.Disable(Silk.NET.OpenGLES.EnableCap.ScissorTest);
            gl.ClearColor(0f, 1f, 0f, 1f);
            gl.Clear((uint)Silk.NET.OpenGLES.GLEnum.ColorBufferBit);
            result = new Basin.Render.Gl.GlBackdropResult(_texture, _width, _height, new Box(0, 0, _width, _height));
            return true;
        }

        private void DestroyTexture()
        {
            if (_texture != 0)
            {
                device.Gl.DeleteFramebuffer(_fbo);
                device.Gl.DeleteTexture(_texture);
                _texture = 0;
                _fbo = 0;
            }
        }

        public void Dispose() => DestroyTexture();
    }

    private static unsafe void Fill(MemoryBuffer buffer, uint pixel)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        for (var y = 0; y < buffer.Height; y++)
        {
            var row = (uint*)(view.Data + y * view.Stride);
            for (var x = 0; x < buffer.Width; x++)
            {
                row[x] = pixel;
            }
        }

        buffer.EndDataAccess();
    }

    private sealed class NotAVulkanEffect : IBackdropEffect
    {
    }

    private sealed unsafe class PaintGreenEffect : IVulkanBackdropEffect, IDisposable
    {
        private readonly VulkanDevice _device;
        private Image _image;
        private DeviceMemory _memory;
        private ImageView _view;
        private Extent2D _extent;

        public PaintGreenEffect(VulkanDevice device) => _device = device;

        public bool Record(in VulkanBackdropContext context, out VulkanBackdropResult result)
        {
            var vk = _device.Api;
            var bounds = context.Bounds;
            EnsureImage((uint)bounds.Width, (uint)bounds.Height);

            var toClear = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                SrcAccessMask = AccessFlags.ShaderReadBit,
                DstAccessMask = AccessFlags.TransferWriteBit,
                OldLayout = ImageLayout.General,
                NewLayout = ImageLayout.General,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = _image,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            vk.CmdPipelineBarrier(
                context.Commands,
                PipelineStageFlags.FragmentShaderBit,
                PipelineStageFlags.TransferBit,
                0, 0, null, 0, null, 1, &toClear);

            var green = new ClearColorValue(0, 1, 0, 1);
            var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
            vk.CmdClearColorImage(context.Commands, _image, ImageLayout.General, in green, 1, in range);

            var toSample = toClear with
            {
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.ShaderReadBit,
            };
            vk.CmdPipelineBarrier(
                context.Commands,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.FragmentShaderBit,
                0, 0, null, 0, null, 1, &toSample);

            result = new VulkanBackdropResult(_view, _extent, new Box(0, 0, bounds.Width, bounds.Height));
            return true;
        }

        private void EnsureImage(uint width, uint height)
        {
            if (_image.Handle != 0 && _extent.Width >= width && _extent.Height >= height)
            {
                return;
            }

            DestroyImage();
            var vk = _device.Api;
            _extent = new Extent2D(width, height);
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = Format.R16G16B16A16Sfloat,
                Extent = new Extent3D(width, height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                InitialLayout = ImageLayout.Undefined,
            };
            VulkanDevice.Check(vk.CreateImage(_device.Device, in imageInfo, null, out _image), "vkCreateImage(test effect)");
            vk.GetImageMemoryRequirements(_device.Device, _image, out var requirements);
            var allocate = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = _device.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
            };
            VulkanDevice.Check(vk.AllocateMemory(_device.Device, in allocate, null, out _memory), "vkAllocateMemory(test effect)");
            VulkanDevice.Check(vk.BindImageMemory(_device.Device, _image, _memory, 0), "vkBindImageMemory(test effect)");
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _image,
                ViewType = ImageViewType.Type2D,
                Format = Format.R16G16B16A16Sfloat,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            VulkanDevice.Check(vk.CreateImageView(_device.Device, in viewInfo, null, out _view), "vkCreateImageView(test effect)");
            var image = _image;
            _device.SubmitImmediate(commands => _device.TransitionToGeneral(commands, image));
        }

        private void DestroyImage()
        {
            if (_image.Handle == 0)
            {
                return;
            }

            var vk = _device.Api;
            _ = vk.DeviceWaitIdle(_device.Device);
            vk.DestroyImageView(_device.Device, _view, null);
            vk.DestroyImage(_device.Device, _image, null);
            vk.FreeMemory(_device.Device, _memory, null);
            _image = default;
        }

        public void Dispose() => DestroyImage();
    }
}
