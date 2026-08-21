using Basin;
using Basin.Render.Vulkan;
using Silk.NET.Vulkan;

namespace TinyComp;

internal sealed unsafe class VulkanBlurEffect : IVulkanBackdropEffect, Basin.Capabilities.IBackgroundEffects, IDisposable
{
    private const int Levels = 3;

    private const float Offset = 1.5f;

    private const int Pad = 64;

    private struct Push
    {
        public float SrcScaleX, SrcScaleY;
        public float SrcInvW, SrcInvH;
        public float HalfpixelX, HalfpixelY;
    }

    private sealed class Level
    {
        public Image Image;
        public DeviceMemory Memory;
        public ImageView View;
        public Framebuffer Framebuffer;
        public DescriptorSet Set;
        public Extent2D Extent;
    }

    private sealed class Pyramid
    {
        public required Level[] Chain;
    }

    private readonly VulkanDevice _device;
    private readonly Sampler _sampler;
    private readonly DescriptorSetLayout _setLayout;
    private readonly DescriptorPool _descriptorPool;
    private readonly PipelineLayout _layout;
    private readonly RenderPass _pass;
    private readonly Pipeline _down;
    private readonly Pipeline _up;
    private readonly Dictionary<(uint Width, uint Height), Pyramid> _pyramids = [];
    private readonly Dictionary<ulong, DescriptorSet> _backdropSets = [];

    public Basin.Capabilities.BackgroundEffects Supported => Basin.Capabilities.BackgroundEffects.Blur;

    public VulkanBlurEffect(VulkanDevice device)
    {
        _device = device;
        var vk = device.Api;

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
        };
        VulkanDevice.Check(vk.CreateSampler(device.Device, in samplerInfo, null, out _sampler), "vkCreateSampler(blur)");

        var sampler = _sampler;
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
            PImmutableSamplers = &sampler,
        };
        var setLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding,
        };
        VulkanDevice.Check(vk.CreateDescriptorSetLayout(device.Device, in setLayoutInfo, null, out _setLayout), "vkCreateDescriptorSetLayout(blur)");

        var poolSize = new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 256);
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 256,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
        };
        VulkanDevice.Check(vk.CreateDescriptorPool(device.Device, in poolInfo, null, out _descriptorPool), "vkCreateDescriptorPool(blur)");

        var pushRange = new PushConstantRange(ShaderStageFlags.FragmentBit, 0, 24);
        var setLayout = _setLayout;
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        VulkanDevice.Check(vk.CreatePipelineLayout(device.Device, in layoutInfo, null, out _layout), "vkCreatePipelineLayout(blur)");

        _pass = CreateRenderPass();
        var vertex = CreateShaderModule("blur.vert.spv");
        var down = CreateShaderModule("blur_down.frag.spv");
        var up = CreateShaderModule("blur_up.frag.spv");
        _down = CreatePipeline(vertex, down);
        _up = CreatePipeline(vertex, up);
        vk.DestroyShaderModule(device.Device, vertex, null);
        vk.DestroyShaderModule(device.Device, down, null);
        vk.DestroyShaderModule(device.Device, up, null);
    }

    public bool Record(in VulkanBackdropContext context, out VulkanBackdropResult result)
    {
        var commands = context.Commands;
        var pyramid = PyramidFor(context.TargetExtent);

        var padded = new Box(context.Bounds.X - Pad, context.Bounds.Y - Pad, context.Bounds.Width + 2 * Pad, context.Bounds.Height + 2 * Pad)
            .Intersect(new Box(0, 0, (int)context.TargetExtent.Width, (int)context.TargetExtent.Height));

        for (var i = 1; i <= Levels; i++)
        {
            var srcExtent = i == 1 ? context.TargetExtent : pyramid.Chain[i - 1].Extent;
            var srcSet = i == 1 ? BackdropSetFor(context.Backdrop) : pyramid.Chain[i - 1].Set;
            BlurPass(commands, _down, pyramid.Chain[i], srcSet, srcExtent, srcScale: 2f, RegionAtLevel(padded, i));
            MakeSampleable(commands, pyramid.Chain[i].Image);
        }

        for (var i = Levels - 1; i >= 0; i--)
        {
            var src = pyramid.Chain[i + 1];
            BlurPass(commands, _up, pyramid.Chain[i], src.Set, src.Extent, srcScale: 0.5f, RegionAtLevel(padded, i));
            MakeSampleable(commands, pyramid.Chain[i].Image);
        }

        result = new VulkanBackdropResult(pyramid.Chain[0].View, pyramid.Chain[0].Extent, context.Bounds);
        return true;
    }

    private static Rect2D RegionAtLevel(in Box padded, int level)
    {
        var x = padded.X >> level;
        var y = padded.Y >> level;
        var right = ((padded.X + padded.Width) >> level) + 1;
        var bottom = ((padded.Y + padded.Height) >> level) + 1;
        return new Rect2D(new Offset2D(x, y), new Extent2D((uint)(right - x), (uint)(bottom - y)));
    }

    private void BlurPass(
        CommandBuffer commands, Pipeline pipeline, Level dst, DescriptorSet srcSet, Extent2D srcExtent, float srcScale, Rect2D region)
    {
        var vk = _device.Api;
        if (region.Offset.X + region.Extent.Width > dst.Extent.Width)
        {
            region.Extent.Width = dst.Extent.Width - (uint)region.Offset.X;
        }

        if (region.Offset.Y + region.Extent.Height > dst.Extent.Height)
        {
            region.Extent.Height = dst.Extent.Height - (uint)region.Offset.Y;
        }

        var passBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _pass,
            Framebuffer = dst.Framebuffer,
            RenderArea = region,
        };
        vk.CmdBeginRenderPass(commands, in passBegin, SubpassContents.Inline);
        vk.CmdBindPipeline(commands, PipelineBindPoint.Graphics, pipeline);
        vk.CmdBindDescriptorSets(commands, PipelineBindPoint.Graphics, _layout, 0, 1, &srcSet, 0, null);
        var viewport = new Viewport(0, 0, dst.Extent.Width, dst.Extent.Height, 0, 1);
        vk.CmdSetViewport(commands, 0, 1, in viewport);
        vk.CmdSetScissor(commands, 0, 1, in region);
        var constants = new Push
        {
            SrcScaleX = srcScale,
            SrcScaleY = srcScale,
            SrcInvW = 1f / srcExtent.Width,
            SrcInvH = 1f / srcExtent.Height,
            HalfpixelX = Offset / srcExtent.Width,
            HalfpixelY = Offset / srcExtent.Height,
        };
        vk.CmdPushConstants(commands, _layout, ShaderStageFlags.FragmentBit, 0, 24, &constants);
        vk.CmdDraw(commands, 3, 1, 0, 0);
        vk.CmdEndRenderPass(commands);
    }

    private void MakeSampleable(CommandBuffer commands, Image image)
    {
        var vk = _device.Api;
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            OldLayout = ImageLayout.General,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        vk.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, 1, &barrier);
    }

    private Pyramid PyramidFor(Extent2D target)
    {
        if (_pyramids.TryGetValue((target.Width, target.Height), out var existing))
        {
            return existing;
        }

        var chain = new Level[Levels + 1];
        for (var i = 0; i <= Levels; i++)
        {
            chain[i] = CreateLevel(new Extent2D(
                Math.Max(1, target.Width >> i),
                Math.Max(1, target.Height >> i)));
        }

        var pyramid = new Pyramid { Chain = chain };
        _pyramids[(target.Width, target.Height)] = pyramid;
        return pyramid;
    }

    private Level CreateLevel(Extent2D extent)
    {
        var vk = _device.Api;
        var level = new Level { Extent = extent };
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R16G16B16A16Sfloat,
            Extent = new Extent3D(extent.Width, extent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            InitialLayout = ImageLayout.Undefined,
        };
        VulkanDevice.Check(vk.CreateImage(_device.Device, in imageInfo, null, out level.Image), "vkCreateImage(blur level)");
        vk.GetImageMemoryRequirements(_device.Device, level.Image, out var requirements);
        var allocate = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = _device.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        VulkanDevice.Check(vk.AllocateMemory(_device.Device, in allocate, null, out level.Memory), "vkAllocateMemory(blur level)");
        VulkanDevice.Check(vk.BindImageMemory(_device.Device, level.Image, level.Memory, 0), "vkBindImageMemory(blur level)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = level.Image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R16G16B16A16Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        VulkanDevice.Check(vk.CreateImageView(_device.Device, in viewInfo, null, out level.View), "vkCreateImageView(blur level)");

        var attachment = level.View;
        var framebufferInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _pass,
            AttachmentCount = 1,
            PAttachments = &attachment,
            Width = extent.Width,
            Height = extent.Height,
            Layers = 1,
        };
        VulkanDevice.Check(vk.CreateFramebuffer(_device.Device, in framebufferInfo, null, out level.Framebuffer), "vkCreateFramebuffer(blur level)");

        level.Set = AllocateSet(level.View);
        var image = level.Image;
        _device.SubmitImmediate(commands => _device.TransitionToGeneral(commands, image));
        return level;
    }

    private DescriptorSet BackdropSetFor(ImageView view)
    {
        if (!_backdropSets.TryGetValue(view.Handle, out var set))
        {
            set = AllocateSet(view);
            _backdropSets[view.Handle] = set;
        }

        return set;
    }

    private DescriptorSet AllocateSet(ImageView view)
    {
        var vk = _device.Api;
        var setLayout = _setLayout;
        var allocateInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout,
        };
        VulkanDevice.Check(vk.AllocateDescriptorSets(_device.Device, in allocateInfo, out var set), "vkAllocateDescriptorSets(blur)");
        var imageInfo = new DescriptorImageInfo(default, view, ImageLayout.General);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo,
        };
        vk.UpdateDescriptorSets(_device.Device, 1, in write, 0, null);
        return set;
    }

    private RenderPass CreateRenderPass()
    {
        var attachment = new AttachmentDescription
        {
            Format = Format.R16G16B16A16Sfloat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.General,
            FinalLayout = ImageLayout.General,
        };
        var colorReference = new AttachmentReference(0, ImageLayout.General);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
        };
        var info = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &attachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
        };
        VulkanDevice.Check(_device.Api.CreateRenderPass(_device.Device, in info, null, out var pass), "vkCreateRenderPass(blur)");
        return pass;
    }

    private Pipeline CreatePipeline(ShaderModule vertex, ShaderModule fragment)
    {
        var vk = _device.Api;
        var entryPoint = (byte*)Silk.NET.Core.Native.SilkMarshal.StringToPtr("main");
        var stages = stackalloc PipelineShaderStageCreateInfo[2];
        stages[0] = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vertex,
            PName = entryPoint,
        };
        stages[1] = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = fragment,
            PName = entryPoint,
        };

        var vertexInput = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
        var inputAssembly = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList,
        };
        var viewportState = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1,
        };
        var rasterization = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullModeFlags.None,
            FrontFace = FrontFace.CounterClockwise,
            LineWidth = 1,
        };
        var multisample = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit,
        };
        var blendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = false,
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
        };
        var blendState = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = 1,
            PAttachments = &blendAttachment,
        };
        var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
        var dynamic = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates,
        };
        var info = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            StageCount = 2,
            PStages = stages,
            PVertexInputState = &vertexInput,
            PInputAssemblyState = &inputAssembly,
            PViewportState = &viewportState,
            PRasterizationState = &rasterization,
            PMultisampleState = &multisample,
            PColorBlendState = &blendState,
            PDynamicState = &dynamic,
            Layout = _layout,
            RenderPass = _pass,
            Subpass = 0,
        };
        VulkanDevice.Check(vk.CreateGraphicsPipelines(_device.Device, default, 1, in info, null, out var pipeline), "vkCreateGraphicsPipelines(blur)");
        Silk.NET.Core.Native.SilkMarshal.Free((nint)entryPoint);
        return pipeline;
    }

    private ShaderModule CreateShaderModule(string resourceName)
    {
        using var stream = typeof(VulkanBlurEffect).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"missing shader resource {resourceName}");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        fixed (byte* codePtr = code)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)codePtr,
            };
            VulkanDevice.Check(_device.Api.CreateShaderModule(_device.Device, in info, null, out var module), $"vkCreateShaderModule({resourceName})");
            return module;
        }
    }

    public void Dispose()
    {
        var vk = _device.Api;
        _ = vk.DeviceWaitIdle(_device.Device);
        foreach (var pyramid in _pyramids.Values)
        {
            foreach (var level in pyramid.Chain)
            {
                vk.DestroyFramebuffer(_device.Device, level.Framebuffer, null);
                vk.DestroyImageView(_device.Device, level.View, null);
                vk.DestroyImage(_device.Device, level.Image, null);
                vk.FreeMemory(_device.Device, level.Memory, null);
            }
        }

        _pyramids.Clear();
        _backdropSets.Clear();
        vk.DestroyPipeline(_device.Device, _down, null);
        vk.DestroyPipeline(_device.Device, _up, null);
        vk.DestroyRenderPass(_device.Device, _pass, null);
        vk.DestroyPipelineLayout(_device.Device, _layout, null);
        vk.DestroyDescriptorPool(_device.Device, _descriptorPool, null);
        vk.DestroyDescriptorSetLayout(_device.Device, _setLayout, null);
        vk.DestroySampler(_device.Device, _sampler, null);
    }
}
