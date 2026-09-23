using Basin.Render.Vulkan;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Basin.UI.Quill;

internal sealed unsafe class QuillVulkanPipeline : IDisposable
{
    internal const Format TargetFormat = Format.B8G8R8A8Unorm;
    internal const uint UniformSize = 248;

    private const uint VertexStride = 20;

    private readonly VulkanDevice _device;
    private bool _disposed;

    internal QuillVulkanPipeline(VulkanDevice device)
    {
        _device = device;
        var vk = device.Api;

        vk.GetPhysicalDeviceProperties(device.Physical, out var properties);
        var alignment = Math.Max(1UL, properties.Limits.MinUniformBufferOffsetAlignment);
        UniformStride = (UniformSize + alignment - 1) / alignment * alignment;

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MaxLod = 0,
        };
        VulkanDevice.Check(vk.CreateSampler(device.Device, in samplerInfo, null, out var sampler), "vkCreateSampler(quill)");
        Sampler = sampler;

        var samplers = stackalloc Sampler[1] { sampler };
        var bindings = stackalloc DescriptorSetLayoutBinding[4];
        bindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBufferDynamic,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
        };
        for (uint i = 1; i < 4; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
                PImmutableSamplers = samplers,
            };
        }

        var setLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4,
            PBindings = bindings,
        };
        VulkanDevice.Check(
            vk.CreateDescriptorSetLayout(device.Device, in setLayoutInfo, null, out var setLayout),
            "vkCreateDescriptorSetLayout(quill)");
        SetLayout = setLayout;

        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
        };
        VulkanDevice.Check(vk.CreatePipelineLayout(device.Device, in layoutInfo, null, out var layout), "vkCreatePipelineLayout(quill)");
        Layout = layout;

        ClearPass = CreateRenderPass(AttachmentLoadOp.Clear);
        LoadPass = CreateRenderPass(AttachmentLoadOp.Load);

        var vertex = CreateShaderModule(QuillShaders.VertexSpirv, "vertex");
        var fragment = CreateShaderModule(QuillShaders.FragmentSpirv, "fragment");
        try
        {
            Pipeline = CreatePipeline(vertex, fragment);
        }
        finally
        {
            vk.DestroyShaderModule(device.Device, vertex, null);
            vk.DestroyShaderModule(device.Device, fragment, null);
        }
    }

    internal ulong UniformStride { get; }

    internal Sampler Sampler { get; }

    internal DescriptorSetLayout SetLayout { get; }

    internal PipelineLayout Layout { get; }

    internal RenderPass ClearPass { get; }

    internal RenderPass LoadPass { get; }

    internal Pipeline Pipeline { get; }

    internal Framebuffer CreateFramebuffer(ImageView view, int width, int height)
    {
        var pass = ClearPass;
        var info = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = pass,
            AttachmentCount = 1,
            PAttachments = &view,
            Width = (uint)width,
            Height = (uint)height,
            Layers = 1,
        };
        VulkanDevice.Check(
            _device.Api.CreateFramebuffer(_device.Device, in info, null, out var framebuffer),
            "vkCreateFramebuffer(quill)");
        return framebuffer;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var vk = _device.Api;
        vk.DestroyPipeline(_device.Device, Pipeline, null);
        vk.DestroyRenderPass(_device.Device, ClearPass, null);
        vk.DestroyRenderPass(_device.Device, LoadPass, null);
        vk.DestroyPipelineLayout(_device.Device, Layout, null);
        vk.DestroyDescriptorSetLayout(_device.Device, SetLayout, null);
        vk.DestroySampler(_device.Device, Sampler, null);
    }

    private RenderPass CreateRenderPass(AttachmentLoadOp load)
    {
        var attachment = new AttachmentDescription
        {
            Format = TargetFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = load,
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
        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.TransferBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.FragmentShaderBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit
                            | AccessFlags.ShaderReadBit,
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
            AttachmentCount = 1,
            PAttachments = &attachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };
        VulkanDevice.Check(_device.Api.CreateRenderPass(_device.Device, in info, null, out var pass), "vkCreateRenderPass(quill)");
        return pass;
    }

    private ShaderModule CreateShaderModule(ReadOnlySpan<byte> code, string stage)
    {
        fixed (byte* codePtr = code)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)codePtr,
            };
            VulkanDevice.Check(
                _device.Api.CreateShaderModule(_device.Device, in info, null, out var module),
                $"vkCreateShaderModule(quill {stage})");
            return module;
        }
    }

    private Pipeline CreatePipeline(ShaderModule vertex, ShaderModule fragment)
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

            var binding = new VertexInputBindingDescription(0, VertexStride, VertexInputRate.Vertex);
            var attributes = stackalloc VertexInputAttributeDescription[3];
            attributes[0] = new VertexInputAttributeDescription(0, 0, Format.R32G32Sfloat, 0);
            attributes[1] = new VertexInputAttributeDescription(1, 0, Format.R32G32Sfloat, 8);
            attributes[2] = new VertexInputAttributeDescription(2, 0, Format.R8G8B8A8Unorm, 16);
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &binding,
                VertexAttributeDescriptionCount = 3,
                PVertexAttributeDescriptions = attributes,
            };
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
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.One,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit
                                 | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
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
                Layout = Layout,
                RenderPass = ClearPass,
                Subpass = 0,
            };
            VulkanDevice.Check(
                _device.Api.CreateGraphicsPipelines(_device.Device, default, 1, in info, null, out var pipeline),
                "vkCreateGraphicsPipelines(quill)");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free((nint)entryPoint);
        }
    }
}
