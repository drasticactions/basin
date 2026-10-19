using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Vulkan;
using Silk.NET.Vulkan;

namespace Basin.Effects;

public sealed unsafe class VulkanBackdropGlass : IVulkanBackdropEffect, IBackdropGlass
{
    private const int PushSize = 128;

    private sealed class Level
    {
        public Image Image;
        public DeviceMemory Memory;
        public ImageView View;
        public Framebuffer Framebuffer;
        public DescriptorSet Set;
        public Extent2D Extent;
    }

    private readonly VulkanDevice _device;
    private readonly VulkanBackdropBlur _blur;
    private readonly Sampler _sampler;
    private readonly DescriptorSetLayout _setLayout;
    private readonly DescriptorPool _descriptorPool;
    private readonly PipelineLayout _layout;
    private readonly RenderPass _pass;
    private readonly Pipeline _composite;
    private readonly Pipeline _fusedDown;
    private readonly Pipeline _fused;
    private readonly Pipeline _copy;
    private readonly Pipeline _meshComposite;
    private readonly Pipeline _meshFused;
    private readonly Dictionary<object, FrameBackdropShape> _shapes = [];
    private readonly Dictionary<(uint Width, uint Height), Level> _outputs = [];
    private readonly Dictionary<(uint Width, uint Height), Level> _reduced = [];
    private readonly Dictionary<ulong, DescriptorSet> _sets = [];
    private GlassOptions _options = new();
    private bool _disposed;

    public VulkanBackdropGlass(VulkanDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _blur = new VulkanBackdropBlur(device);
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
        VulkanDevice.Check(vk.CreateSampler(device.Device, in samplerInfo, null, out _sampler), "vkCreateSampler(glass)");

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
        VulkanDevice.Check(vk.CreateDescriptorSetLayout(device.Device, in setLayoutInfo, null, out _setLayout), "vkCreateDescriptorSetLayout(glass)");

        var poolSize = new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 256);
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = 256,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
        };
        VulkanDevice.Check(vk.CreateDescriptorPool(device.Device, in poolInfo, null, out _descriptorPool), "vkCreateDescriptorPool(glass)");

        var pushRange = new PushConstantRange(ShaderStageFlags.FragmentBit, 0, PushSize);
        var setLayouts = stackalloc DescriptorSetLayout[2] { _setLayout, _setLayout };
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 2,
            PSetLayouts = setLayouts,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        VulkanDevice.Check(vk.CreatePipelineLayout(device.Device, in layoutInfo, null, out _layout), "vkCreatePipelineLayout(glass)");

        _pass = CreateRenderPass();
        var vertex = CreateShaderModule("blur.vert.spv");
        var composite = CreateShaderModule("glass.frag.spv");
        var fusedDown = CreateShaderModule("glass_fused_down.frag.spv");
        var fused = CreateShaderModule("glass_fused.frag.spv");
        var copy = CreateShaderModule("glass_copy.frag.spv");
        var meshVertex = CreateShaderModule("glass_mesh.vert.spv");
        var meshComposite = CreateShaderModule("glass_mesh.frag.spv");
        var meshFused = CreateShaderModule("glass_mesh_fused.frag.spv");
        _composite = CreatePipeline(vertex, composite);
        _fusedDown = CreatePipeline(vertex, fusedDown);
        _fused = CreatePipeline(vertex, fused);
        _copy = CreatePipeline(vertex, copy);
        _meshComposite = CreatePipeline(meshVertex, meshComposite, mesh: true);
        _meshFused = CreatePipeline(meshVertex, meshFused, mesh: true);
        vk.DestroyShaderModule(device.Device, copy, null);
        vk.DestroyShaderModule(device.Device, meshVertex, null);
        vk.DestroyShaderModule(device.Device, meshComposite, null);
        vk.DestroyShaderModule(device.Device, meshFused, null);
        vk.DestroyShaderModule(device.Device, vertex, null);
        vk.DestroyShaderModule(device.Device, composite, null);
        vk.DestroyShaderModule(device.Device, fusedDown, null);
        vk.DestroyShaderModule(device.Device, fused, null);
        Options = _options;
        _device.ImageViewDestroying += ForgetView;
        BasinCounters.Track();
    }

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

    public int ExpandSize => _blur.ExpandSize;

    public void SetShape(object key, in FrameBackdropShape shape)
    {
        ArgumentNullException.ThrowIfNull(key);
        _shapes[key] = shape;
    }

    public bool ForgetSurface(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _shapes.Remove(key);
    }

    public bool Record(in VulkanBackdropContext context, out VulkanBackdropResult result)
    {
        result = default;
        var target = context.TargetExtent;
        var bounds = context.Bounds.Intersect(new Box(0, 0, (int)target.Width, (int)target.Height));
        if (bounds.IsEmpty)
        {
            return false;
        }

        var shape = context.Key is { } key && _shapes.TryGetValue(key, out var stored) ? stored : default;
        var mesh = shape.Width > 0 && shape.Height > 0 ? context.Mesh.Span : default;
        var field = mesh.IsEmpty
            ? GlassFrame.For(_options, shape, context.Extent.IsEmpty ? context.Bounds : context.Extent, bounds, context.LinearLight)
            : GlassFrame.ForLocal(_options, shape, bounds, context.LinearLight);
        var vertices = mesh.IsEmpty ? default : UploadMesh(mesh, target);
        var plain = SetFor(context.Backdrop);
        var output = LevelFor(_outputs, target);
        var commands = context.Commands;
        if (_options.Pass == GlassPass.Fused)
        {
            var factor = GlassFrame.Factor(_options.Strength, out var sigma);
            var small = LevelFor(
                _reduced,
                new Extent2D((uint)(((int)target.Width + factor - 1) / factor), (uint)(((int)target.Height + factor - 1) / factor)));
            var smallRegion = new Box(
                bounds.X / factor,
                bounds.Y / factor,
                ((bounds.Right + factor - 1) / factor) - (bounds.X / factor),
                ((bounds.Bottom + factor - 1) / factor) - (bounds.Y / factor));
            Draw(commands, _fusedDown, small, smallRegion, plain, plain, field with { Extra = field.Extra with { Z = factor } });
            var fusedField = field with { Extra = field.Extra with { Z = factor, W = (float)sigma } };
            if (!vertices.IsValid)
            {
                Draw(commands, _fused, output, bounds, small.Set, plain, fusedField);
            }
            else
            {
                Draw(commands, _copy, output, bounds, small.Set, plain, fusedField);
                Draw(commands, _meshFused, output, bounds, small.Set, plain, fusedField, vertices, mesh.Length);
            }
        }
        else
        {
            if (!_blur.Record(context with { Mesh = default }, out var blurred) || blurred.View.Handle == 0)
            {
                return false;
            }

            var blurredSet = SetFor(blurred.View);
            if (!vertices.IsValid)
            {
                Draw(commands, _composite, output, bounds, blurredSet, plain, field);
            }
            else
            {
                Draw(commands, _copy, output, bounds, blurredSet, plain, field);
                Draw(commands, _meshComposite, output, bounds, blurredSet, plain, field, vertices, mesh.Length);
            }
        }

        result = new VulkanBackdropResult(output.View, output.Extent, bounds) { Image = output.Image };
        return true;
    }

    private StagingSpan UploadMesh(ReadOnlySpan<MeshVertex> mesh, Extent2D target)
    {
        var span = _device.Staging.Allocate((ulong)(mesh.Length * 4 * sizeof(float)), 16);
        if (!span.IsValid)
        {
            return default;
        }

        var mapped = (float*)span.Mapped;
        for (var i = 0; i < mesh.Length; i++)
        {
            var vertex = mesh[i];
            mapped[(i * 4) + 0] = (vertex.X / target.Width * 2f) - 1f;
            mapped[(i * 4) + 1] = (vertex.Y / target.Height * 2f) - 1f;
            mapped[(i * 4) + 2] = vertex.U;
            mapped[(i * 4) + 3] = vertex.V;
        }

        return span;
    }

    private void Draw(
        CommandBuffer commands, Pipeline pipeline, Level dst, in Box region, DescriptorSet src, DescriptorSet plain, GlassFrame field,
        StagingSpan vertices = default, int vertexCount = 0)
    {
        var vk = _device.Api;
        var width = Math.Min(region.Width, (int)dst.Extent.Width - region.X);
        var height = Math.Min(region.Height, (int)dst.Extent.Height - region.Y);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var area = new Rect2D(new Offset2D(region.X, region.Y), new Extent2D((uint)width, (uint)height));
        var passBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _pass,
            Framebuffer = dst.Framebuffer,
            RenderArea = area,
        };
        vk.CmdBeginRenderPass(commands, in passBegin, SubpassContents.Inline);
        vk.CmdBindPipeline(commands, PipelineBindPoint.Graphics, pipeline);
        var sets = stackalloc DescriptorSet[2] { src, plain };
        vk.CmdBindDescriptorSets(commands, PipelineBindPoint.Graphics, _layout, 0, 2, sets, 0, null);
        var viewport = new Viewport(0, 0, dst.Extent.Width, dst.Extent.Height, 0, 1);
        vk.CmdSetViewport(commands, 0, 1, in viewport);
        vk.CmdSetScissor(commands, 0, 1, in area);
        vk.CmdPushConstants(commands, _layout, ShaderStageFlags.FragmentBit, 0, PushSize, &field);
        if (vertices.IsValid)
        {
            var buffer = vertices.Buffer;
            var offset = vertices.Offset;
            vk.CmdBindVertexBuffers(commands, 0, 1, &buffer, &offset);
            vk.CmdDraw(commands, (uint)vertexCount, 1, 0, 0);
        }
        else
        {
            vk.CmdDraw(commands, 3, 1, 0, 0);
        }
        vk.CmdEndRenderPass(commands);

        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            OldLayout = ImageLayout.General,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = dst.Image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        vk.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, 1, &barrier);
    }

    private DescriptorSet SetFor(ImageView view)
    {
        if (_sets.TryGetValue(view.Handle, out var set))
        {
            return set;
        }

        set = AllocateSet(view);
        _sets[view.Handle] = set;
        return set;
    }

    private void ForgetView(ImageView view)
    {
        if (_sets.Remove(view.Handle, out var set))
        {
            _ = _device.Api.FreeDescriptorSets(_device.Device, _descriptorPool, 1, in set);
        }
    }

    private Level LevelFor(Dictionary<(uint Width, uint Height), Level> cache, Extent2D extent)
    {
        if (cache.TryGetValue((extent.Width, extent.Height), out var existing))
        {
            return existing;
        }

        if (cache.Count >= 4)
        {
            DestroyLevels(cache);
        }

        var level = CreateLevel(extent);
        cache[(extent.Width, extent.Height)] = level;
        return level;
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
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit
                | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit,
            InitialLayout = ImageLayout.Undefined,
        };
        VulkanDevice.Check(vk.CreateImage(_device.Device, in imageInfo, null, out level.Image), "vkCreateImage(glass)");
        vk.GetImageMemoryRequirements(_device.Device, level.Image, out var requirements);
        var allocate = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = _device.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        VulkanDevice.Check(vk.AllocateMemory(_device.Device, in allocate, null, out level.Memory), "vkAllocateMemory(glass)");
        VulkanDevice.Check(vk.BindImageMemory(_device.Device, level.Image, level.Memory, 0), "vkBindImageMemory(glass)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = level.Image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R16G16B16A16Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        VulkanDevice.Check(vk.CreateImageView(_device.Device, in viewInfo, null, out level.View), "vkCreateImageView(glass)");

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
        VulkanDevice.Check(vk.CreateFramebuffer(_device.Device, in framebufferInfo, null, out level.Framebuffer), "vkCreateFramebuffer(glass)");
        level.Set = AllocateSet(level.View);
        var image = level.Image;
        _device.SubmitImmediate(commands =>
        {
            _device.TransitionToGeneral(commands, image);
            var zero = default(ClearColorValue);
            var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
            _device.Api.CmdClearColorImage(commands, image, ImageLayout.General, in zero, 1, in range);
        });
        return level;
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
        VulkanDevice.Check(vk.AllocateDescriptorSets(_device.Device, in allocateInfo, out var set), "vkAllocateDescriptorSets(glass)");
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
        VulkanDevice.Check(_device.Api.CreateRenderPass(_device.Device, in info, null, out var pass), "vkCreateRenderPass(glass)");
        return pass;
    }

    private Pipeline CreatePipeline(ShaderModule vertex, ShaderModule fragment, bool mesh = false)
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

        var meshBinding = new VertexInputBindingDescription(0, 4 * sizeof(float), VertexInputRate.Vertex);
        var meshAttributes = stackalloc VertexInputAttributeDescription[2]
        {
            new(0, 0, Format.R32G32Sfloat, 0),
            new(1, 0, Format.R32G32Sfloat, 2 * sizeof(float)),
        };
        var vertexInput = mesh
            ? new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &meshBinding,
                VertexAttributeDescriptionCount = 2,
                PVertexAttributeDescriptions = meshAttributes,
            }
            : new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
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
        VulkanDevice.Check(vk.CreateGraphicsPipelines(_device.Device, default, 1, in info, null, out var pipeline), "vkCreateGraphicsPipelines(glass)");
        Silk.NET.Core.Native.SilkMarshal.Free((nint)entryPoint);
        return pipeline;
    }

    private ShaderModule CreateShaderModule(string resourceName)
    {
        using var stream = typeof(VulkanBackdropGlass).Assembly.GetManifestResourceStream(resourceName)
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

    private void DestroyLevels(Dictionary<(uint Width, uint Height), Level> cache)
    {
        var vk = _device.Api;
        _ = vk.DeviceWaitIdle(_device.Device);
        foreach (var level in cache.Values)
        {
            _device.NotifyImageViewDestroying(level.View);
            _ = vk.FreeDescriptorSets(_device.Device, _descriptorPool, 1, in level.Set);
            vk.DestroyFramebuffer(_device.Device, level.Framebuffer, null);
            vk.DestroyImageView(_device.Device, level.View, null);
            vk.DestroyImage(_device.Device, level.Image, null);
            vk.FreeMemory(_device.Device, level.Memory, null);
        }

        cache.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        _device.ImageViewDestroying -= ForgetView;
        var vk = _device.Api;
        _ = vk.DeviceWaitIdle(_device.Device);
        DestroyLevels(_outputs);
        DestroyLevels(_reduced);
        _sets.Clear();
        vk.DestroyPipeline(_device.Device, _composite, null);
        vk.DestroyPipeline(_device.Device, _fusedDown, null);
        vk.DestroyPipeline(_device.Device, _fused, null);
        vk.DestroyPipeline(_device.Device, _copy, null);
        vk.DestroyPipeline(_device.Device, _meshComposite, null);
        vk.DestroyPipeline(_device.Device, _meshFused, null);
        vk.DestroyRenderPass(_device.Device, _pass, null);
        vk.DestroyPipelineLayout(_device.Device, _layout, null);
        vk.DestroyDescriptorPool(_device.Device, _descriptorPool, null);
        vk.DestroyDescriptorSetLayout(_device.Device, _setLayout, null);
        vk.DestroySampler(_device.Device, _sampler, null);
        _blur.Dispose();
    }
}
