using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Pixman;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Basin.Render.Vulkan;

internal sealed unsafe class VulkanRenderPass : IRenderPass
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public float DstX, DstY, DstW, DstH;
        public float SrcX, SrcY, SrcW, SrcH;
        public float TargetW, TargetH;
        public float Alpha;
        public float ForceOpaque;
        public float R, G, B, A;
        public float T00, T10, T20, TPad0;
        public float T01, T11, T21, TPad1;
        public float T02, T12, T22, TPad2;
    }

    private static void WriteTransform(ref Push constants, in RenderTransform transform)
    {
        constants.T00 = (float)transform.M11;
        constants.T10 = (float)transform.M21;
        constants.T20 = (float)transform.M31;
        constants.T01 = (float)transform.M12;
        constants.T11 = (float)transform.M22;
        constants.T21 = (float)transform.M32;
        constants.T02 = (float)transform.M13;
        constants.T12 = (float)transform.M23;
        constants.T22 = (float)transform.M33;
    }

    private readonly VulkanRenderer _renderer;
    private readonly Silk.NET.Vulkan.Extensions.KHR.KhrExternalSemaphoreFd? _semaphoreFd;
    private readonly List<VulkanDmabufTexture> _foreign = [];
    private readonly List<Semaphore> _waitSemaphores = [];
    private int _waitCount;
    private CommandBuffer _render;
    private CommandBuffer _stage;
    private int _boundKind = -1;
    private Rect2D _area;
    private IBuffer? _target;
    private RenderTarget? _entry;
    private int _waitFenceFd = -1;
    private int _signalFenceFd = -1;

    internal VulkanRenderPass(VulkanRenderer renderer)
    {
        _renderer = renderer;
        if (renderer.Dev.EnabledExtensions.Contains("VK_KHR_external_semaphore_fd") &&
            renderer.Dev.Api.TryGetDeviceExtension(renderer.Dev.Instance, renderer.Dev.Device, out Silk.NET.Vulkan.Extensions.KHR.KhrExternalSemaphoreFd ext))
        {
            _semaphoreFd = ext;
        }
    }

    internal void Begin(IBuffer target, RenderTarget entry, int waitFenceFd, int signalFenceFd)
    {
        if (_target is not null)
        {
            throw new InvalidOperationException("The previous render pass was not submitted.");
        }

        _target = target;
        _entry = entry;
        _waitFenceFd = waitFenceFd;
        _signalFenceFd = signalFenceFd;
        _foreign.Clear();
        _waitCount = 0;
        _stage = default;
        _boundKind = -1;

        var vk = _renderer.Dev.Api;
        _render = _renderer.Dev.Ring.Acquire();

        _renderer.Dev.Staging.Recycle(_renderer.Dev.Ring.CompletedPoint);
        if (!entry.IsCpuReadback)
        {
            _renderer.Dev.AcquireImported(_render, entry.Image);
        }

        _area = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)target.Width, (uint)target.Height));
        var passBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = entry.TwoPassTarget ? _renderer.TwoPass : _renderer.OnePass,
            Framebuffer = entry.Framebuffer,
            RenderArea = _area,
        };
        vk.CmdBeginRenderPass(_render, in passBegin, SubpassContents.Inline);
        var viewport = new Viewport(0, 0, target.Width, target.Height, 0, 1);
        vk.CmdSetViewport(_render, 0, 1, in viewport);
    }

    public void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (box.IsEmpty)
        {
            return;
        }

        var constants = new Push
        {
            DstX = box.X,
            DstY = box.Y,
            DstW = box.Width,
            DstH = box.Height,
            TargetW = _target.Width,
            TargetH = _target.Height,
            R = LinearizePremultiplied(color.R, color.A),
            G = LinearizePremultiplied(color.G, color.A),
            B = LinearizePremultiplied(color.B, color.A),
            A = color.A,
        };
        WriteTransform(ref constants, RenderTransform.Identity);
        Record(0, default, default, constants, clip);
    }

    public void AddShader(IPixelShader shader, in ShaderRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (shader is not VulkanPixelShader vulkanShader)
        {
            throw new ArgumentException("shader does not belong to this renderer");
        }

        if (vulkanShader.SamplesTexture)
        {
            throw new ArgumentException("shader samples a texture and must draw through AddTexture");
        }

        if (options.DstBox.IsEmpty)
        {
            return;
        }

        var constants = new Push
        {
            DstX = options.DstBox.X,
            DstY = options.DstBox.Y,
            DstW = options.DstBox.Width,
            DstH = options.DstBox.Height,
            TargetW = _target.Width,
            TargetH = _target.Height,
            Alpha = options.Alpha,
        };
        WriteTransform(ref constants, RenderTransform.Identity);
        RecordShader(vulkanShader, default, srgbDecode: false, options.DstBox, options.Alpha, constants, options.Clip);
    }

    private void RecordShader(
        VulkanPixelShader shader,
        DescriptorSet textureSet,
        bool srgbDecode,
        in Box dstBox,
        float alpha,
        in Push constants,
        PixmanRegion32? clip)
    {
        var span = _renderer.Dev.Staging.Allocate(VulkanRenderer.ShaderBlockCapacity, 256);
        if (!span.IsValid)
        {
            return;
        }

        shader.WriteBlock(span.Mapped, dstBox.Width, dstBox.Height, alpha);
        var uboSet = _renderer.UboSetFor(span.Buffer);
        var vk = _renderer.Dev.Api;
        vk.CmdBindPipeline(_render, PipelineBindPoint.Graphics, shader.PipelineFor(_entry!.TwoPassTarget, srgbDecode));
        _boundKind = -1;

        var layout = shader.SamplesTexture ? _renderer.ShaderTextureLayout : _renderer.ShaderLayout;
        var uboSlot = 0u;
        if (shader.SamplesTexture)
        {
            vk.CmdBindDescriptorSets(_render, PipelineBindPoint.Graphics, layout, 0, 1, &textureSet, 0, null);
            uboSlot = 1;
        }

        var dynamicOffset = (uint)span.Offset;
        vk.CmdBindDescriptorSets(_render, PipelineBindPoint.Graphics, layout, uboSlot, 1, &uboSet, 1, &dynamicOffset);
        fixed (Push* constantsPtr = &constants)
        {
            vk.CmdPushConstants(
                _render,
                layout,
                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                0,
                (uint)sizeof(Push),
                constantsPtr);
        }

        if (clip is null)
        {
            vk.CmdSetScissor(_render, 0, 1, in _area);
            vk.CmdDraw(_render, 4, 1, 0, 0);
        }
        else
        {
            foreach (var rect in RegionRects.Of(clip))
            {
                var scissor = new Rect2D(
                    new Offset2D(rect.X1, rect.Y1),
                    new Extent2D((uint)(rect.X2 - rect.X1), (uint)(rect.Y2 - rect.Y1)));
                vk.CmdSetScissor(_render, 0, 1, in scissor);
                vk.CmdDraw(_render, 4, 1, 0, 0);
            }
        }
    }

    public void AddTexture(ITexture texture, in TextureRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (options.DstBox.IsEmpty || (!options.Transform.IsIdentity && !options.Transform.TryInvert(out _)))
        {
            return;
        }

        if (options.Shader is not null)
        {
            if (options.Shader is not VulkanPixelShader textureShader)
            {
                throw new ArgumentException("shader does not belong to this renderer");
            }

            if (!textureShader.SamplesTexture)
            {
                throw new ArgumentException("shader does not sample a texture");
            }
        }

        var hasLutOption = options.Lut is VulkanColorLut;
        DescriptorSet set;
        bool forceOpaque;
        int kind;
        switch (texture)
        {
            case VulkanDmabufTexture dmabuf:
                if (!dmabuf.OwnedThisPass)
                {
                    dmabuf.OwnedThisPass = true;
                    _foreign.Add(dmabuf);
                }

                if (dmabuf.Ycbcr is not null)
                {
                    if (options.Shader is not null)
                    {
                        throw new ArgumentException("shader cannot sample a YCbCr texture on this renderer");
                    }

                    RecordYcbcr(dmabuf, options);
                    return;
                }

                set = hasLutOption ? dmabuf.Set : dmabuf.LinearSet;
                kind = hasLutOption ? 3 : dmabuf.NeedsShaderDecode ? 2 : 1;
                forceOpaque = !dmabuf.HasAlpha;
                break;
            case VulkanShmTexture shm:
                if (!shm.PrepareUpload(_renderer.Dev.Staging))
                {
                    return;
                }

                if (shm.NeedsGpuCopy)
                {
                    shm.RecordGpuCopy(EnsureStage());
                }

                set = hasLutOption ? shm.Set : shm.LinearSet;
                kind = hasLutOption ? 3 : shm.NeedsShaderDecode ? 2 : 1;
                forceOpaque = !shm.HasAlpha;
                break;
            default:
                throw new ArgumentException("texture does not belong to this renderer");
        }

        var src = options.SrcBox.IsEmpty
            ? new FBox(0, 0, texture.Width, texture.Height)
            : options.SrcBox;
        var constants = new Push
        {
            DstX = options.DstBox.X,
            DstY = options.DstBox.Y,
            DstW = options.DstBox.Width,
            DstH = options.DstBox.Height,
            SrcX = (float)(src.X / texture.Width),
            SrcY = (float)(src.Y / texture.Height),
            SrcW = (float)(src.Width / texture.Width),
            SrcH = (float)(src.Height / texture.Height),
            TargetW = _target.Width,
            TargetH = _target.Height,
            Alpha = options.Alpha,
            ForceOpaque = forceOpaque ? 1f : 0f,
        };
        WriteTransform(ref constants, options.Transform);
        if (options.Shader is VulkanPixelShader vulkanShader && !hasLutOption)
        {
            RecordShader(vulkanShader, set, srgbDecode: kind == 2, options.DstBox, options.Alpha, constants, options.Clip);
            return;
        }

        Record(
            kind,
            set,
            options.Lut is VulkanColorLut vulkanLut ? vulkanLut.Set : default,
            constants,
            options.Clip,
            options.Opaque && options.Alpha >= 1f);
    }

    public void AddMesh(ITexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (vertices.Length == 0)
        {
            return;
        }

        if (vertices.Length % 3 != 0)
        {
            throw new ArgumentException("vertices must be a whole number of triangles", nameof(vertices));
        }

        var mode = 0;
        DescriptorSet set = default;
        var forceOpaque = false;
        float textureWidth = 1, textureHeight = 1;
        switch (texture)
        {
            case null:
                break;
            case VulkanDmabufTexture dmabuf:
                if (dmabuf.Ycbcr is not null)
                {
                    return;
                }

                if (!dmabuf.OwnedThisPass)
                {
                    dmabuf.OwnedThisPass = true;
                    _foreign.Add(dmabuf);
                }

                set = dmabuf.LinearSet;
                mode = dmabuf.NeedsShaderDecode ? 2 : 1;
                forceOpaque = !dmabuf.HasAlpha;
                textureWidth = texture.Width;
                textureHeight = texture.Height;
                break;
            case VulkanShmTexture shm:
                if (!shm.PrepareUpload(_renderer.Dev.Staging))
                {
                    return;
                }

                if (shm.NeedsGpuCopy)
                {
                    shm.RecordGpuCopy(EnsureStage());
                }

                set = shm.LinearSet;
                mode = shm.NeedsShaderDecode ? 2 : 1;
                forceOpaque = !shm.HasAlpha;
                textureWidth = texture.Width;
                textureHeight = texture.Height;
                break;
            default:
                throw new ArgumentException("texture does not belong to this renderer");
        }

        var byteSize = (ulong)(vertices.Length * sizeof(MeshVertex));
        var span = _renderer.Dev.Staging.Allocate(byteSize, 16);
        if (!span.IsValid)
        {
            return;
        }

        var mapped = (MeshVertex*)span.Mapped;
        for (var i = 0; i < vertices.Length; i++)
        {
            var vertex = vertices[i];
            mapped[i] = vertex with
            {
                Color = new RenderColor(
                    LinearizePremultiplied(vertex.Color.R, vertex.Color.A),
                    LinearizePremultiplied(vertex.Color.G, vertex.Color.A),
                    LinearizePremultiplied(vertex.Color.B, vertex.Color.A),
                    vertex.Color.A),
            };
        }

        var vk = _renderer.Dev.Api;
        var group = _entry!.TwoPassTarget ? _renderer.TwoPassMesh : _renderer.OnePassMesh;
        vk.CmdBindPipeline(_render, PipelineBindPoint.Graphics, group.For(options.Blend, mode));
        _boundKind = -1;

        if (mode != 0)
        {
            vk.CmdBindDescriptorSets(_render, PipelineBindPoint.Graphics, _renderer.Layout, 0, 1, &set, 0, null);
        }

        var constants = new Push
        {
            SrcW = textureWidth,
            SrcH = textureHeight,
            TargetW = _target.Width,
            TargetH = _target.Height,
            ForceOpaque = forceOpaque ? 1f : 0f,
        };
        WriteTransform(ref constants, RenderTransform.Identity);
        vk.CmdPushConstants(
            _render,
            _renderer.Layout,
            ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            0,
            (uint)sizeof(Push),
            &constants);

        var vertexBuffer = span.Buffer;
        var vertexOffset = span.Offset;
        vk.CmdBindVertexBuffers(_render, 0, 1, &vertexBuffer, &vertexOffset);

        if (options.Clip is null)
        {
            vk.CmdSetScissor(_render, 0, 1, in _area);
            vk.CmdDraw(_render, (uint)vertices.Length, 1, 0, 0);
        }
        else
        {
            foreach (var rect in RegionRects.Of(options.Clip))
            {
                var scissor = new Rect2D(
                    new Offset2D(rect.X1, rect.Y1),
                    new Extent2D((uint)(rect.X2 - rect.X1), (uint)(rect.Y2 - rect.Y1)));
                vk.CmdSetScissor(_render, 0, 1, in scissor);
                vk.CmdDraw(_render, (uint)vertices.Length, 1, 0, 0);
            }
        }
    }

    private void RecordYcbcr(VulkanDmabufTexture texture, in TextureRenderOptions options)
    {
        var vk = _renderer.Dev.Api;
        var bundle = texture.Ycbcr!;
        var pipeline = _entry!.TwoPassTarget ? bundle.TwoPassPipeline : bundle.OnePassPipeline;
        vk.CmdBindPipeline(_render, PipelineBindPoint.Graphics, pipeline);
        _boundKind = -1;

        var set = texture.Set;
        vk.CmdBindDescriptorSets(_render, PipelineBindPoint.Graphics, bundle.PipelineLayout, 0, 1, &set, 0, null);

        var src = options.SrcBox.IsEmpty
            ? new FBox(0, 0, texture.Width, texture.Height)
            : options.SrcBox;
        var constants = new Push
        {
            DstX = options.DstBox.X,
            DstY = options.DstBox.Y,
            DstW = options.DstBox.Width,
            DstH = options.DstBox.Height,
            SrcX = (float)(src.X / texture.Width),
            SrcY = (float)(src.Y / texture.Height),
            SrcW = (float)(src.Width / texture.Width),
            SrcH = (float)(src.Height / texture.Height),
            TargetW = _target!.Width,
            TargetH = _target.Height,
            Alpha = options.Alpha,
            ForceOpaque = 1f,
        };
        WriteTransform(ref constants, options.Transform);
        vk.CmdPushConstants(
            _render,
            bundle.PipelineLayout,
            ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            0,
            (uint)sizeof(Push),
            &constants);

        if (options.Clip is null)
        {
            vk.CmdSetScissor(_render, 0, 1, in _area);
            vk.CmdDraw(_render, 4, 1, 0, 0);
        }
        else
        {
            foreach (var rect in RegionRects.Of(options.Clip))
            {
                var scissor = new Rect2D(
                    new Offset2D(rect.X1, rect.Y1),
                    new Extent2D((uint)(rect.X2 - rect.X1), (uint)(rect.Y2 - rect.Y1)));
                vk.CmdSetScissor(_render, 0, 1, in scissor);
                vk.CmdDraw(_render, 4, 1, 0, 0);
            }
        }
    }

    public void AddBackdropEffect(IBackdropEffect effect, in Box bounds, PixmanRegion32? clip = null, object? key = null)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (effect is not IVulkanBackdropEffect vulkanEffect)
        {
            throw new ArgumentException("effect does not belong to this renderer");
        }

        var entry = _entry!;
        if (bounds.IsEmpty || !entry.CanSampleBackdrop)
        {
            return;
        }

        var vk = _renderer.Dev.Api;

        if (entry.TwoPassTarget)
        {
            vk.CmdNextSubpass(_render, SubpassContents.Inline);
        }

        vk.CmdEndRenderPass(_render);

        var composite = entry.TwoPassTarget ? entry.BlendImage : entry.Image;
        var drawn = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            OldLayout = ImageLayout.General,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = composite,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        vk.CmdPipelineBarrier(
            _render,
            PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, 1, &drawn);

        var context = new VulkanBackdropContext
        {
            Device = _renderer.Dev,
            Commands = _render,
            Backdrop = entry.TwoPassTarget ? entry.BlendView : entry.SrgbView,
            TargetExtent = new Extent2D((uint)_target.Width, (uint)_target.Height),
            Bounds = bounds,
            Key = key,
        };
        var recorded = vulkanEffect.Record(in context, out var result);

        var transformed = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.ShaderWriteBit | AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
        };
        vk.CmdPipelineBarrier(
            _render,
            PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.TransferBit,
            PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ColorAttachmentOutputBit,
            0, 1, &transformed, 0, null, 0, null);

        var passBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = entry.TwoPassTarget ? _renderer.TwoPass : _renderer.OnePass,
            Framebuffer = entry.Framebuffer,
            RenderArea = _area,
        };
        vk.CmdBeginRenderPass(_render, in passBegin, SubpassContents.Inline);
        var viewport = new Viewport(0, 0, _target.Width, _target.Height, 0, 1);
        vk.CmdSetViewport(_render, 0, 1, in viewport);
        _boundKind = -1;

        if (!recorded)
        {
            return;
        }

        var constants = new Push
        {
            DstX = bounds.X,
            DstY = bounds.Y,
            DstW = bounds.Width,
            DstH = bounds.Height,
            SrcX = (float)result.Source.X / result.Extent.Width,
            SrcY = (float)result.Source.Y / result.Extent.Height,
            SrcW = (float)result.Source.Width / result.Extent.Width,
            SrcH = (float)result.Source.Height / result.Extent.Height,
            TargetW = _target.Width,
            TargetH = _target.Height,
            Alpha = 1f,
            ForceOpaque = 1f,
        };
        WriteTransform(ref constants, RenderTransform.Identity);
        Record(1, _renderer.EffectSetFor(result.View), default, constants, clip);
    }

    public bool AddFrameFilter(IFrameFilter filter, ITexture source, in FrameFilterOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (filter is not IVulkanFilter vulkanFilter)
        {
            throw new ArgumentException("filter does not belong to this renderer");
        }

        var entry = _entry!;
        if (entry.TwoPassTarget || !vulkanFilter.IsSupported)
        {
            return false;
        }

        Image sourceImage;
        Format sourceFormat;
        switch (source)
        {
            case VulkanDmabufTexture dmabuf:
                if (dmabuf.Ycbcr is not null)
                {
                    return false;
                }

                if (!dmabuf.OwnedThisPass)
                {
                    dmabuf.OwnedThisPass = true;
                    _foreign.Add(dmabuf);
                }

                sourceImage = dmabuf.Image;
                sourceFormat = dmabuf.VkFormat;
                break;
            case VulkanShmTexture shm:
                if (!shm.PrepareUpload(_renderer.Dev.Staging))
                {
                    return false;
                }

                if (shm.NeedsGpuCopy)
                {
                    shm.RecordGpuCopy(EnsureStage());
                }

                sourceImage = shm.Image;
                sourceFormat = shm.VkFormat;
                break;
            default:
                throw new ArgumentException("texture does not belong to this renderer");
        }

        var vk = _renderer.Dev.Api;
        vk.CmdEndRenderPass(_render);

        var into = stackalloc ImageMemoryBarrier[2];
        into[0] = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            OldLayout = ImageLayout.General,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = sourceImage,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        into[1] = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
            OldLayout = ImageLayout.General,
            NewLayout = ImageLayout.ColorAttachmentOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = entry.Image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        vk.CmdPipelineBarrier(
            _render,
            PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ColorAttachmentOutputBit,
            0, 0, null, 0, null, 2, into);

        var context = new VulkanFilterContext
        {
            Device = _renderer.Dev,
            Commands = _render,
            Source = sourceImage,
            SourceFormat = sourceFormat,
            SourceExtent = new Extent2D((uint)source.Width, (uint)source.Height),
            Target = entry.Image,
            TargetFormat = Format.B8G8R8A8Unorm,
            TargetExtent = new Extent2D((uint)_target.Width, (uint)_target.Height),
            Viewport = new Box(0, 0, _target.Width, _target.Height),
            Options = options,
        };
        var recorded = vulkanFilter.Record(in context);

        var back = stackalloc ImageMemoryBarrier[2];
        back[0] = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderReadBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
            OldLayout = ImageLayout.ShaderReadOnlyOptimal,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = sourceImage,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        back[1] = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
            OldLayout = ImageLayout.ColorAttachmentOptimal,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = entry.Image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        vk.CmdPipelineBarrier(
            _render,
            PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.AllCommandsBit,
            0, 0, null, 0, null, 2, back);

        var passBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderer.OnePass,
            Framebuffer = entry.Framebuffer,
            RenderArea = _area,
        };
        vk.CmdBeginRenderPass(_render, in passBegin, SubpassContents.Inline);
        var viewport = new Viewport(0, 0, _target.Width, _target.Height, 0, 1);
        vk.CmdSetViewport(_render, 0, 1, in viewport);
        _boundKind = -1;

        return recorded;
    }

    private static float LinearizePremultiplied(float premultiplied, float alpha)
    {
        var straight = alpha > 0 ? Math.Clamp(premultiplied / alpha, 0f, 1f) : 0f;
        var linear = straight <= 0.04045f ? straight / 12.92f : MathF.Pow((straight + 0.055f) / 1.055f, 2.4f);
        return linear * alpha;
    }

    private void Record(int kind, DescriptorSet set, DescriptorSet lutSet, in Push constants, PixmanRegion32? clip, bool opaque = false)
    {
        var vk = _renderer.Dev.Api;
        var bindKey = opaque ? kind + 8 : kind;
        if (bindKey != _boundKind)
        {
            var group = _entry!.TwoPassTarget ? _renderer.TwoPassPipelines : _renderer.OnePassPipelines;
            vk.CmdBindPipeline(
                _render,
                PipelineBindPoint.Graphics,
                opaque
                    ? kind switch { 1 => group.TextureIdentityOpaque, 2 => group.TextureSrgbOpaque, _ => group.TextureLutOpaque }
                    : kind switch { 0 => group.Solid, 1 => group.TextureIdentity, 2 => group.TextureSrgb, _ => group.TextureLut });
            _boundKind = bindKey;
        }

        var layout = kind == 3 ? _renderer.LutLayout : _renderer.Layout;
        if (kind != 0)
        {
            vk.CmdBindDescriptorSets(_render, PipelineBindPoint.Graphics, layout, 0, 1, &set, 0, null);
            if (kind == 3)
            {
                vk.CmdBindDescriptorSets(_render, PipelineBindPoint.Graphics, layout, 1, 1, &lutSet, 0, null);
            }
        }

        fixed (Push* constantsPtr = &constants)
        {
            vk.CmdPushConstants(
                _render,
                layout,
                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                0,
                (uint)sizeof(Push),
                constantsPtr);
        }

        if (clip is null)
        {
            vk.CmdSetScissor(_render, 0, 1, in _area);
            vk.CmdDraw(_render, 4, 1, 0, 0);
        }
        else
        {
            foreach (var rect in RegionRects.Of(clip))
            {
                var scissor = new Rect2D(
                    new Offset2D(rect.X1, rect.Y1),
                    new Extent2D((uint)(rect.X2 - rect.X1), (uint)(rect.Y2 - rect.Y1)));
                vk.CmdSetScissor(_render, 0, 1, in scissor);
                vk.CmdDraw(_render, 4, 1, 0, 0);
            }
        }
    }

    public bool Submit()
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        var vk = _renderer.Dev.Api;
        var target = _target;
        var entry = _entry!;
        _target = null;
        _entry = null;

        if (entry.TwoPassTarget)
        {
            vk.CmdNextSubpass(_render, SubpassContents.Inline);
            vk.CmdBindPipeline(_render, PipelineBindPoint.Graphics, _renderer.OutputPipeline);
            var inputSet = entry.BlendSet.Set;
            vk.CmdBindDescriptorSets(_render, PipelineBindPoint.Graphics, _renderer.OutputPipeLayout, 0, 1, &inputSet, 0, null);
            var encode = new Push
            {
                DstW = target.Width,
                DstH = target.Height,
                TargetW = target.Width,
                TargetH = target.Height,
            };
            WriteTransform(ref encode, RenderTransform.Identity);
            vk.CmdPushConstants(
                _render,
                _renderer.OutputPipeLayout,
                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                0,
                (uint)sizeof(Push),
                &encode);
            vk.CmdSetScissor(_render, 0, 1, in _area);
            vk.CmdDraw(_render, 4, 1, 0, 0);
        }

        vk.CmdEndRenderPass(_render);

        foreach (var texture in _foreign)
        {
            _renderer.Dev.ReleaseImported(_render, texture.Image);
        }

        if (!entry.IsCpuReadback)
        {
            _renderer.Dev.ReleaseImported(_render, entry.Image);
        }
        else
        {
            var rendered = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit,
                OldLayout = ImageLayout.General,
                NewLayout = ImageLayout.General,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = entry.Image,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            vk.CmdPipelineBarrier(
                _render,
                PipelineStageFlags.ColorAttachmentOutputBit,
                PipelineStageFlags.TransferBit,
                0, 0, null, 0, null, 1, &rendered);

            var copy = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D((uint)target.Width, (uint)target.Height, 1),
            };
            vk.CmdCopyImageToBuffer(_render, entry.Image, ImageLayout.General, entry.Readback, 1, &copy);

            var toHost = new MemoryBarrier
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.HostReadBit,
            };
            vk.CmdPipelineBarrier(
                _render,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.HostBit,
                0, 1, &toHost, 0, null, 0, null);
        }

        foreach (var texture in _foreign)
        {
            texture.OwnedThisPass = false;
            _renderer.Dev.AcquireImported(EnsureStage(), texture.Image);
            var attributes = texture.Attributes;
            for (var plane = 0; plane < attributes.PlaneCount; plane++)
            {
                AddWaitFd(RenderFences.ExportDmabufSyncFile(attributes.Fds[plane], forWrite: false));
            }
        }

        if (!entry.IsCpuReadback && entry.Imported is not null)
        {
            var attributes = entry.Attributes;
            for (var plane = 0; plane < attributes.PlaneCount; plane++)
            {
                AddWaitFd(RenderFences.ExportDmabufSyncFile(attributes.Fds[plane], forWrite: true));
            }
        }

        if (_waitFenceFd >= 0)
        {
            AddWaitFd(Libc.Dup(_waitFenceFd));
            _waitFenceFd = -1;
        }

        var waits = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_waitSemaphores)[.._waitCount];
        var render = _render;
        var submitted = _renderer.Dev.Ring.TrySubmitFrame(_stage, _render, waits, out var point);
        if (submitted)
        {
            _renderer.Dev.Staging.MarkSubmitted(point);
        }

        _stage = default;
        _render = default;
        if (!submitted)
        {
            _signalFenceFd = -1;
            _foreign.Clear();
            return false;
        }

        var published = false;
        if (!entry.IsCpuReadback)
        {
            var syncFile = _renderer.Dev.Ring.ExportSyncFile(render);
            if (syncFile >= 0)
            {
                published = true;

                _renderer.ReplaceCompletionFence(Libc.Dup(syncFile));
                var attributes = entry.Attributes;
                for (var plane = 0; plane < attributes.PlaneCount; plane++)
                {
                    published &= RenderFences.ImportDmabufSyncFile(attributes.Fds[plane], forWrite: true, syncFile);
                }

                foreach (var texture in _foreign)
                {
                    var read = texture.Attributes;
                    for (var plane = 0; plane < read.PlaneCount; plane++)
                    {
                        _ = RenderFences.ImportDmabufSyncFile(read.Fds[plane], forWrite: false, syncFile);
                    }
                }

                Libc.Close(syncFile);
            }
        }

        _foreign.Clear();

        if (entry.IsCpuReadback || _signalFenceFd >= 0 || !published)
        {
            _renderer.Dev.Ring.Wait(point);
        }

        var usable = !entry.IsCpuReadback || ReadBack(target, entry);
        if (_signalFenceFd >= 0)
        {
            RenderFences.SignalSyncobjFd(_renderer.Dev.DrmFd, _signalFenceFd);
            _signalFenceFd = -1;
        }

        return usable;
    }

    private CommandBuffer EnsureStage()
    {
        if (_stage.Handle == 0)
        {
            _stage = _renderer.Dev.Ring.Acquire();
        }

        return _stage;
    }

    private void AddWaitFd(int fd)
    {
        if (fd < 0)
        {
            return;
        }

        if (_semaphoreFd is { } ext)
        {
            if (_waitCount == _waitSemaphores.Count)
            {
                var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
                VulkanDevice.Check(
                    _renderer.Dev.Api.CreateSemaphore(_renderer.Dev.Device, in semaphoreInfo, null, out var fresh),
                    "vkCreateSemaphore(wait)");
                _waitSemaphores.Add(fresh);
            }

            var import = new Silk.NET.Vulkan.ImportSemaphoreFdInfoKHR
            {
                SType = StructureType.ImportSemaphoreFDInfoKhr,
                Semaphore = _waitSemaphores[_waitCount],
                Flags = SemaphoreImportFlags.TemporaryBit,
                HandleType = ExternalSemaphoreHandleTypeFlags.SyncFDBit,
                Fd = fd,
            };
            if (ext.ImportSemaphoreF(_renderer.Dev.Device, in import) == Result.Success)
            {
                _waitCount++;
                return;
            }
        }

        _ = RenderFences.WaitSyncFile(fd);
        Libc.Close(fd);
    }

    private static bool ReadBack(IBuffer target, RenderTarget entry)
    {
        if (!target.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            return false;
        }

        try
        {
            var rowBytes = target.Width * 4;
            for (var y = 0; y < target.Height; y++)
            {
                System.Buffer.MemoryCopy(
                    (byte*)entry.ReadbackMapped + y * rowBytes,
                    (void*)(view.Data + y * view.Stride),
                    rowBytes,
                    rowBytes);
            }
        }
        finally
        {
            target.EndDataAccess();
        }

        return true;
    }

    internal void DestroyResources()
    {
        foreach (var semaphore in _waitSemaphores)
        {
            _renderer.Dev.Api.DestroySemaphore(_renderer.Dev.Device, semaphore, null);
        }

        _waitSemaphores.Clear();
    }
}
