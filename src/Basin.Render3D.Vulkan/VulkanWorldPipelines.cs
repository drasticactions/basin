using Basin.Render.Vulkan;
using Basin.Scene3D;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Basin.Render3D.Vulkan;

internal sealed unsafe class VulkanWorldPipelines : IDisposable
{
    public const Format ColorFormat = Format.B8G8R8A8Unorm;
    public const uint DrawSize = 208;
    public const uint SceneSize = 304;
    public const uint JointsSize = Skeleton.MaxJoints * 64;

    private readonly VulkanDevice _device;
    private readonly Pipeline[] _pipelines = new Pipeline[(int)VulkanWorldPipeline.Count];
    private bool _disposed;

    public VulkanWorldPipelines(VulkanDevice device, float anisotropy)
    {
        _device = device;
        var vk = device.Api;
        vk.GetPhysicalDeviceProperties(device.Physical, out var properties);
        var alignment = Math.Max(1UL, properties.Limits.MinUniformBufferOffsetAlignment);
        DrawStride = Align(DrawSize, alignment);
        SceneStride = Align(SceneSize, alignment);
        JointsStride = Align(JointsSize, alignment);
        MaxAnisotropy = device.SamplerAnisotropy ? properties.Limits.MaxSamplerAnisotropy : 1;
        DepthFormat = PickDepthFormat();

        Anisotropy = anisotropy;
        WindowSampler = CreateSampler(Filter.Linear, SamplerMipmapMode.Linear, SamplerAddressMode.ClampToEdge, anisotropy);
        DirectSampler = CreateSampler(Filter.Linear, SamplerMipmapMode.Nearest, SamplerAddressMode.ClampToEdge, 0, maxLod: 0);
        AtlasSampler = CreateSampler(Filter.Linear, SamplerMipmapMode.Linear, SamplerAddressMode.Repeat, anisotropy);

        var bindings = stackalloc DescriptorSetLayoutBinding[4];
        bindings[0] = new DescriptorSetLayoutBinding(0, DescriptorType.UniformBufferDynamic, 1, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit);
        bindings[1] = new DescriptorSetLayoutBinding(1, DescriptorType.UniformBufferDynamic, 1, ShaderStageFlags.FragmentBit);
        bindings[2] = new DescriptorSetLayoutBinding(2, DescriptorType.CombinedImageSampler, 1, ShaderStageFlags.FragmentBit);
        bindings[3] = new DescriptorSetLayoutBinding(3, DescriptorType.UniformBufferDynamic, 1, ShaderStageFlags.VertexBit);
        SetLayout = CreateSetLayout(bindings, 4);
        Layout = CreateLayout(SetLayout, 0);

        var copyBinding = new DescriptorSetLayoutBinding(0, DescriptorType.CombinedImageSampler, 1, ShaderStageFlags.FragmentBit);
        CopySetLayout = CreateSetLayout(&copyBinding, 1);
        CopyLayout = CreateLayout(CopySetLayout, 32);

        MainPass = CreateMainPass();
        CopyPass = CreateCopyPass();

        var worldVertex = CreateModule("world.vert");
        var skinnedVertex = CreateModule("world_skinned.vert");
        var worldFragment = CreateModule("world.frag");
        var flatVertex = CreateModule("flat.vert");
        var flatFragment = CreateModule("flat.frag");
        var copyVertex = CreateModule("copy.vert");
        var copyFragment = CreateModule("copy.frag");
        try
        {
            _pipelines[(int)VulkanWorldPipeline.LitOpaque] = Create(worldVertex, worldFragment, Vertex.World, Depth.Write, Stencil.Keep, true, CullModeFlags.BackBit, true);
            _pipelines[(int)VulkanWorldPipeline.LitTranslucent] = Create(worldVertex, worldFragment, Vertex.World, Depth.Test, Stencil.Keep, true, CullModeFlags.BackBit, true);
            _pipelines[(int)VulkanWorldPipeline.LitTwoSided] = Create(worldVertex, worldFragment, Vertex.World, Depth.Write, Stencil.Keep, true, CullModeFlags.None, true);
            _pipelines[(int)VulkanWorldPipeline.Skinned] = Create(skinnedVertex, worldFragment, Vertex.Skinned, Depth.Write, Stencil.Keep, true, CullModeFlags.None, true);
            _pipelines[(int)VulkanWorldPipeline.StencilIncrement] = Create(flatVertex, flatFragment, Vertex.World, Depth.Test, Stencil.Increment, false, CullModeFlags.None, true);
            _pipelines[(int)VulkanWorldPipeline.StencilReset] = Create(flatVertex, flatFragment, Vertex.World, Depth.Always, Stencil.Keep, true, CullModeFlags.None, false);
            _pipelines[(int)VulkanWorldPipeline.StencilDecrement] = Create(flatVertex, flatFragment, Vertex.World, Depth.Always, Stencil.Decrement, false, CullModeFlags.None, true);
            CopyPipeline = CreateCopy(copyVertex, copyFragment);
        }
        finally
        {
            foreach (var module in new[] { worldVertex, skinnedVertex, worldFragment, flatVertex, flatFragment, copyVertex, copyFragment })
            {
                vk.DestroyShaderModule(device.Device, module, null);
            }
        }
    }

    private enum Vertex
    {
        World,
        Skinned,
    }

    private enum Depth
    {
        Write,
        Test,
        Always,
    }

    private enum Stencil
    {
        Keep,
        Increment,
        Decrement,
    }

    public ulong DrawStride { get; }

    public ulong SceneStride { get; }

    public ulong JointsStride { get; }

    public float MaxAnisotropy { get; }

    public Format DepthFormat { get; }

    public float Anisotropy { get; private set; }

    public Sampler WindowSampler { get; private set; }

    public Sampler DirectSampler { get; }

    public Sampler AtlasSampler { get; private set; }

    public DescriptorSetLayout SetLayout { get; }

    public PipelineLayout Layout { get; }

    public DescriptorSetLayout CopySetLayout { get; }

    public PipelineLayout CopyLayout { get; }

    public RenderPass MainPass { get; }

    public RenderPass CopyPass { get; }

    public Pipeline CopyPipeline { get; }

    public Pipeline this[VulkanWorldPipeline kind] => _pipelines[(int)kind];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var vk = _device.Api;
        foreach (var pipeline in _pipelines)
        {
            vk.DestroyPipeline(_device.Device, pipeline, null);
        }

        vk.DestroyPipeline(_device.Device, CopyPipeline, null);
        vk.DestroyRenderPass(_device.Device, MainPass, null);
        vk.DestroyRenderPass(_device.Device, CopyPass, null);
        vk.DestroyPipelineLayout(_device.Device, Layout, null);
        vk.DestroyPipelineLayout(_device.Device, CopyLayout, null);
        vk.DestroyDescriptorSetLayout(_device.Device, SetLayout, null);
        vk.DestroyDescriptorSetLayout(_device.Device, CopySetLayout, null);
        vk.DestroySampler(_device.Device, WindowSampler, null);
        vk.DestroySampler(_device.Device, DirectSampler, null);
        vk.DestroySampler(_device.Device, AtlasSampler, null);
    }

    private static ulong Align(ulong size, ulong alignment) => (size + alignment - 1) / alignment * alignment;

    private Format PickDepthFormat()
    {
        foreach (var format in new[] { Format.D24UnormS8Uint, Format.D32SfloatS8Uint })
        {
            _device.Api.GetPhysicalDeviceFormatProperties(_device.Physical, format, out var properties);
            if ((properties.OptimalTilingFeatures & FormatFeatureFlags.DepthStencilAttachmentBit) != 0)
            {
                return format;
            }
        }

        throw new InvalidOperationException($"The device at {_device.DevicePath} has no depth and stencil attachment format.");
    }

    public void ReplaceSamplers(float anisotropy, out Sampler oldWindow, out Sampler oldAtlas)
    {
        oldWindow = WindowSampler;
        oldAtlas = AtlasSampler;
        Anisotropy = anisotropy;
        WindowSampler = CreateSampler(Filter.Linear, SamplerMipmapMode.Linear, SamplerAddressMode.ClampToEdge, anisotropy);
        AtlasSampler = CreateSampler(Filter.Linear, SamplerMipmapMode.Linear, SamplerAddressMode.Repeat, anisotropy);
    }

    private Sampler CreateSampler(Filter filter, SamplerMipmapMode mipmap, SamplerAddressMode address, float anisotropy, float maxLod = 1000)
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = filter,
            MinFilter = filter,
            MipmapMode = mipmap,
            AddressModeU = address,
            AddressModeV = address,
            AddressModeW = address,
            AnisotropyEnable = anisotropy > 1 && MaxAnisotropy > 1,
            MaxAnisotropy = Math.Clamp(anisotropy, 1, MaxAnisotropy),
            MinLod = 0,
            MaxLod = maxLod,
        };
        VulkanDevice.Check(_device.Api.CreateSampler(_device.Device, in info, null, out var sampler), "vkCreateSampler(world)");
        return sampler;
    }

    private DescriptorSetLayout CreateSetLayout(DescriptorSetLayoutBinding* bindings, uint count)
    {
        var info = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = count,
            PBindings = bindings,
        };
        VulkanDevice.Check(_device.Api.CreateDescriptorSetLayout(_device.Device, in info, null, out var layout), "vkCreateDescriptorSetLayout(world)");
        return layout;
    }

    private PipelineLayout CreateLayout(DescriptorSetLayout setLayout, uint pushBytes)
    {
        var push = new PushConstantRange(ShaderStageFlags.FragmentBit, 0, pushBytes);
        var info = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = pushBytes > 0 ? 1u : 0u,
            PPushConstantRanges = pushBytes > 0 ? &push : null,
        };
        VulkanDevice.Check(_device.Api.CreatePipelineLayout(_device.Device, in info, null, out var layout), "vkCreatePipelineLayout(world)");
        return layout;
    }

    private RenderPass CreateMainPass()
    {
        var attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = ColorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.General,
            FinalLayout = ImageLayout.General,
        };
        attachments[1] = new AttachmentDescription
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.Clear,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };
        var color = new AttachmentReference(0, ImageLayout.General);
        var depth = new AttachmentReference(1, ImageLayout.DepthStencilAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &color,
            PDepthStencilAttachment = &depth,
        };
        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.LateFragmentTestsBit | PipelineStageFlags.TransferBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.FragmentShaderBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit |
                AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.ShaderReadBit,
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.AllCommandsBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
        };
        var info = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };
        VulkanDevice.Check(_device.Api.CreateRenderPass(_device.Device, in info, null, out var pass), "vkCreateRenderPass(world)");
        return pass;
    }

    private RenderPass CreateCopyPass()
    {
        var attachment = new AttachmentDescription
        {
            Format = ColorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ColorAttachmentOptimal,
        };
        var color = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &color,
        };
        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.TransferBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.FragmentShaderBit,
            SrcAccessMask = AccessFlags.ShaderReadBit | AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.ShaderReadBit,
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.TransferBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.TransferReadBit,
        };
        var info = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &attachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };
        VulkanDevice.Check(_device.Api.CreateRenderPass(_device.Device, in info, null, out var pass), "vkCreateRenderPass(world copy)");
        return pass;
    }

    private ShaderModule CreateModule(string name)
    {
        var code = VulkanWorldShaders.Load(name);
        fixed (byte* data = code)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)data,
            };
            VulkanDevice.Check(_device.Api.CreateShaderModule(_device.Device, in info, null, out var module), $"vkCreateShaderModule(world {name})");
            return module;
        }
    }

    private Pipeline Create(
        ShaderModule vertex, ShaderModule fragment, Vertex input, Depth depth, Stencil stencil, bool colorWrite,
        CullModeFlags cull, bool blend)
    {
        var bindingStride = input == Vertex.Skinned ? (uint)sizeof(SkinnedVertex) : (uint)sizeof(WorldVertex);
        var binding = new VertexInputBindingDescription(0, bindingStride, VertexInputRate.Vertex);
        var attributes = stackalloc VertexInputAttributeDescription[5];
        attributes[0] = new VertexInputAttributeDescription(0, 0, Format.R32G32B32Sfloat, 0);
        attributes[1] = new VertexInputAttributeDescription(1, 0, Format.R32G32B32Sfloat, 12);
        attributes[2] = new VertexInputAttributeDescription(2, 0, Format.R32G32Sfloat, 24);
        attributes[3] = new VertexInputAttributeDescription(3, 0, Format.R32G32B32A32Sfloat, 32);
        attributes[4] = new VertexInputAttributeDescription(4, 0, Format.R32G32B32A32Sfloat, 48);
        var vertexInput = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = &binding,
            VertexAttributeDescriptionCount = input == Vertex.Skinned ? 5u : 3u,
            PVertexAttributeDescriptions = attributes,
        };
        var stencilOp = new StencilOpState
        {
            FailOp = StencilOp.Keep,
            PassOp = stencil switch
            {
                Stencil.Increment => StencilOp.IncrementAndClamp,
                Stencil.Decrement => StencilOp.DecrementAndClamp,
                _ => StencilOp.Keep,
            },
            DepthFailOp = StencilOp.Keep,
            CompareOp = CompareOp.Equal,
            CompareMask = 0xFF,
            WriteMask = 0xFF,
        };
        var depthStencil = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = depth != Depth.Test,
            DepthCompareOp = depth == Depth.Always ? CompareOp.Always : CompareOp.Less,
            StencilTestEnable = true,
            Front = stencilOp,
            Back = stencilOp,
        };
        var blendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = blend,
            SrcColorBlendFactor = BlendFactor.One,
            DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
            ColorBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = BlendFactor.One,
            DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
            AlphaBlendOp = BlendOp.Add,
            ColorWriteMask = colorWrite
                ? ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
                : 0,
        };
        return Build(vertex, fragment, &vertexInput, &depthStencil, &blendAttachment, cull, Layout, MainPass,
            stackalloc DynamicState[3] { DynamicState.Viewport, DynamicState.Scissor, DynamicState.StencilReference });
    }

    private Pipeline CreateCopy(ShaderModule vertex, ShaderModule fragment)
    {
        var vertexInput = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
        var depthStencil = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo };
        var blendAttachment = new PipelineColorBlendAttachmentState
        {
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
        };
        return Build(vertex, fragment, &vertexInput, &depthStencil, &blendAttachment, CullModeFlags.None, CopyLayout, CopyPass,
            stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor }, PrimitiveTopology.TriangleStrip);
    }

    private Pipeline Build(
        ShaderModule vertex, ShaderModule fragment, PipelineVertexInputStateCreateInfo* vertexInput,
        PipelineDepthStencilStateCreateInfo* depthStencil, PipelineColorBlendAttachmentState* blend, CullModeFlags cull,
        PipelineLayout layout, RenderPass pass, Span<DynamicState> dynamicStates,
        PrimitiveTopology topology = PrimitiveTopology.TriangleList)
    {
        var entryPoint = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
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
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = topology,
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
                CullMode = cull,
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1,
            };
            var multisample = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };
            var blendState = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = blend,
            };
            fixed (DynamicState* states = dynamicStates)
            {
                var dynamic = new PipelineDynamicStateCreateInfo
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = (uint)dynamicStates.Length,
                    PDynamicStates = states,
                };
                var info = new GraphicsPipelineCreateInfo
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = stages,
                    PVertexInputState = vertexInput,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterization,
                    PMultisampleState = &multisample,
                    PDepthStencilState = depthStencil,
                    PColorBlendState = &blendState,
                    PDynamicState = &dynamic,
                    Layout = layout,
                    RenderPass = pass,
                    Subpass = 0,
                };
                VulkanDevice.Check(
                    _device.Api.CreateGraphicsPipelines(_device.Device, default, 1, in info, null, out var pipeline),
                    "vkCreateGraphicsPipelines(world)");
                return pipeline;
            }
        }
        finally
        {
            SilkMarshal.Free((nint)entryPoint);
        }
    }
}
