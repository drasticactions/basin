using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Vulkan;
using Silk.NET.Vulkan;

namespace Basin.Effects;

public sealed unsafe class VulkanBackdropBlur : IVulkanBackdropEffect, IBackdropBlur
{
    private const int PushSize = 128;

    private struct Push
    {
        public float SrcScale;
        public float OpacityValue;
        public float IntensityValue;
        public float Reserved;
        public float HalfpixelX, HalfpixelY;
        public float Reserved2X, Reserved2Y;
        public float ColorMatrix0X, ColorMatrix0Y, ColorMatrix0Z, ColorMatrix0W;
        public float ColorMatrix1X, ColorMatrix1Y, ColorMatrix1Z, ColorMatrix1W;
        public float ColorMatrix2X, ColorMatrix2Y, ColorMatrix2Z, ColorMatrix2W;
        public float BoxX, BoxY, BoxHalfWidth, BoxHalfHeight;
        public float CornerTopLeft, CornerTopRight, CornerBottomLeft, CornerBottomRight;
        public float FrostR, FrostG, FrostB, FrostA;
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
    private readonly Sampler _noiseSampler;
    private readonly DescriptorSetLayout _setLayout;
    private readonly DescriptorSetLayout _noiseSetLayout;
    private readonly DescriptorPool _descriptorPool;
    private readonly PipelineLayout _layout;
    private readonly RenderPass _pass;
    private readonly Pipeline _down;
    private readonly Pipeline _up;
    private readonly Pipeline _onscreen;
    private readonly Dictionary<(uint Width, uint Height), Pyramid> _pyramids = [];
    private readonly Dictionary<ulong, DescriptorSet> _backdropSets = [];
    private readonly float[] _colorMatrix = new float[BlurColorMatrix.Length];
    private Image _noiseImage;
    private DeviceMemory _noiseMemory;
    private ImageView _noiseView;
    private DescriptorSet _noiseSet;
    private int _noiseStrength = -1;
    private int _noiseSide;
    private readonly Dictionary<object, BlurSurfaceOptions> _surfaces = [];
    private readonly float[] _surfaceMatrix = new float[BlurColorMatrix.Length];
    private DescriptorSet _plainSet;
    private Extent2D _targetExtent;
    private Box _surface;
    private BlurSurfaceOptions _current = new();
    private BlurOptions _options = new();
    private BlurStrength _strength = BlurStrength.For(new BlurOptions().Strength);
    private bool _disposed;

    public BackgroundEffects Supported => BackgroundEffects.Blur | BackgroundEffects.Contrast;

    public int ExpandSize => _strength.ExpandSize;

    public BlurCorners Corners { get; set; }

    public double Opacity { get; set; } = 1.0;

    public void SetSurface(object key, in BlurSurfaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(key);
        _surfaces[key] = options;
    }

    public bool ForgetSurface(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _surfaces.Remove(key);
    }

    public BlurOptions Options
    {
        get => _options;
        set
        {
            var iterations = _strength.Iterations;
            _options = value;
            _strength = BlurStrength.For(value.Strength);
            BlurColorMatrix.Build(value.Saturation, value.Contrast, _colorMatrix);
            if (_noiseSide != BlurNoise.SizeFor(value.NoiseScale))
            {
                DestroyNoiseImage();
                CreateNoiseImage();
                _noiseStrength = -1;
            }

            UploadNoise();
            if (_strength.Iterations != iterations)
            {
                DropPyramids();
            }
        }
    }

    public VulkanBackdropBlur(VulkanDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
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

        var noiseSamplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,
            MinFilter = Filter.Nearest,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
        };
        VulkanDevice.Check(vk.CreateSampler(device.Device, in noiseSamplerInfo, null, out _noiseSampler), "vkCreateSampler(blur noise)");

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

        var noiseSampler = _noiseSampler;
        var noiseBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
            PImmutableSamplers = &noiseSampler,
        };
        var noiseSetLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &noiseBinding,
        };
        VulkanDevice.Check(vk.CreateDescriptorSetLayout(device.Device, in noiseSetLayoutInfo, null, out _noiseSetLayout), "vkCreateDescriptorSetLayout(blur noise)");

        var poolSize = new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 256);
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 256,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
        };
        VulkanDevice.Check(vk.CreateDescriptorPool(device.Device, in poolInfo, null, out _descriptorPool), "vkCreateDescriptorPool(blur)");

        var pushRange = new PushConstantRange(ShaderStageFlags.FragmentBit, 0, PushSize);
        var setLayouts = stackalloc DescriptorSetLayout[3] { _setLayout, _noiseSetLayout, _setLayout };
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 3,
            PSetLayouts = setLayouts,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        VulkanDevice.Check(vk.CreatePipelineLayout(device.Device, in layoutInfo, null, out _layout), "vkCreatePipelineLayout(blur)");

        _pass = CreateRenderPass();
        var vertex = CreateShaderModule("blur.vert.spv");
        var down = CreateShaderModule("blur_down.frag.spv");
        var up = CreateShaderModule("blur_up.frag.spv");
        var onscreen = CreateShaderModule("blur_onscreen.frag.spv");
        _down = CreatePipeline(vertex, down);
        _up = CreatePipeline(vertex, up);
        _onscreen = CreatePipeline(vertex, onscreen);
        vk.DestroyShaderModule(device.Device, vertex, null);
        vk.DestroyShaderModule(device.Device, down, null);
        vk.DestroyShaderModule(device.Device, up, null);
        vk.DestroyShaderModule(device.Device, onscreen, null);
        BlurColorMatrix.Build(_options.Saturation, _options.Contrast, _colorMatrix);
        CreateNoiseImage();
        UploadNoise();
        BasinCounters.Track();
    }

    public bool Record(in VulkanBackdropContext context, out VulkanBackdropResult result)
    {
        var commands = context.Commands;
        _plainSet = BackdropSetFor(context.Backdrop);
        _targetExtent = context.TargetExtent;
        _surface = context.Bounds;
        _current = context.Key is { } key && _surfaces.TryGetValue(key, out var stored) ? stored : new BlurSurfaceOptions();
        BuildSurfaceMatrix();
        var levels = _strength.Iterations;
        var pad = _strength.ExpandSize;
        var pyramid = PyramidFor(context.TargetExtent);

        var padded = new Box(context.Bounds.X - pad, context.Bounds.Y - pad, context.Bounds.Width + (2 * pad), context.Bounds.Height + (2 * pad))
            .Intersect(new Box(0, 0, (int)context.TargetExtent.Width, (int)context.TargetExtent.Height));

        if (!_current.Blur)
        {
            BlurPass(
                commands, _onscreen, pyramid.Chain[0], _plainSet,
                srcScale: 1f, RegionAtLevel(padded, 0), plain: true);
            MakeSampleable(commands, pyramid.Chain[0].Image);
            result = new VulkanBackdropResult(pyramid.Chain[0].View, pyramid.Chain[0].Extent, context.Bounds);
            return true;
        }

        for (var i = 1; i <= levels; i++)
        {
            var srcSet = i == 1 ? BackdropSetFor(context.Backdrop) : pyramid.Chain[i - 1].Set;
            BlurPass(commands, _down, pyramid.Chain[i], srcSet, srcScale: 2f, RegionAtLevel(padded, i));
            MakeSampleable(commands, pyramid.Chain[i].Image);
        }

        for (var i = levels - 1; i >= 0; i--)
        {
            var src = pyramid.Chain[i + 1];
            BlurPass(
                commands, i == 0 ? _onscreen : _up, pyramid.Chain[i], src.Set,
                srcScale: 0.5f, RegionAtLevel(padded, i));
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
        CommandBuffer commands, Pipeline pipeline, Level dst, DescriptorSet srcSet, float srcScale, Rect2D region, bool plain = false)
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
        var sets = stackalloc DescriptorSet[3] { srcSet, _noiseSet, _plainSet };
        vk.CmdBindDescriptorSets(commands, PipelineBindPoint.Graphics, _layout, 0, 3, sets, 0, null);
        var viewport = new Viewport(0, 0, dst.Extent.Width, dst.Extent.Height, 0, 1);
        vk.CmdSetViewport(commands, 0, 1, in viewport);
        vk.CmdSetScissor(commands, 0, 1, in region);
        var halfpixel = plain ? 0f : (float)(0.5 * _strength.Offset);
        var corners = _current.Corners.IsSquare ? Corners : _current.Corners;
        var parameters = _current.ContrastParameters;
        var frost = _current.Contrast && parameters.Frost ? parameters.FrostColor : 0u;
        var constants = new Push
        {
            SrcScale = srcScale,
            OpacityValue = (float)Math.Clamp(Opacity * _current.Opacity, 0, 1),
            IntensityValue = _current.Contrast ? (float)parameters.Intensity : 1f,
            HalfpixelX = halfpixel,
            HalfpixelY = halfpixel,
            BoxX = (float)(_surface.X + (_surface.Width / 2.0)),
            BoxY = (float)(_surface.Y + (_surface.Height / 2.0)),
            BoxHalfWidth = (float)(_surface.Width / 2.0),
            BoxHalfHeight = (float)(_surface.Height / 2.0),
            CornerTopLeft = (float)corners.TopLeft,
            CornerTopRight = (float)corners.TopRight,
            CornerBottomLeft = (float)corners.BottomLeft,
            CornerBottomRight = (float)corners.BottomRight,
            FrostR = ((frost >> 16) & 0xFF) / 255f,
            FrostG = ((frost >> 8) & 0xFF) / 255f,
            FrostB = (frost & 0xFF) / 255f,
            FrostA = ((frost >> 24) & 0xFF) / 255f,
            ColorMatrix0X = _surfaceMatrix[0],
            ColorMatrix0Y = _surfaceMatrix[1],
            ColorMatrix0Z = _surfaceMatrix[2],
            ColorMatrix0W = _surfaceMatrix[3],
            ColorMatrix1X = _surfaceMatrix[4],
            ColorMatrix1Y = _surfaceMatrix[5],
            ColorMatrix1Z = _surfaceMatrix[6],
            ColorMatrix1W = _surfaceMatrix[7],
            ColorMatrix2X = _surfaceMatrix[8],
            ColorMatrix2Y = _surfaceMatrix[9],
            ColorMatrix2Z = _surfaceMatrix[10],
            ColorMatrix2W = _surfaceMatrix[11],
        };
        vk.CmdPushConstants(commands, _layout, ShaderStageFlags.FragmentBit, 0, PushSize, &constants);
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

        var chain = new Level[_strength.Iterations + 1];
        for (var i = 0; i <= _strength.Iterations; i++)
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
        using var stream = typeof(VulkanBackdropBlur).Assembly.GetManifestResourceStream(resourceName)
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        var vk = _device.Api;
        _ = vk.DeviceWaitIdle(_device.Device);
        DropPyramids();
        _backdropSets.Clear();
        vk.DestroyPipeline(_device.Device, _down, null);
        vk.DestroyPipeline(_device.Device, _up, null);
        vk.DestroyPipeline(_device.Device, _onscreen, null);
        vk.DestroyRenderPass(_device.Device, _pass, null);
        vk.DestroyPipelineLayout(_device.Device, _layout, null);
        vk.DestroyDescriptorPool(_device.Device, _descriptorPool, null);
        vk.DestroyDescriptorSetLayout(_device.Device, _setLayout, null);
        vk.DestroySampler(_device.Device, _sampler, null);
        vk.DestroySampler(_device.Device, _noiseSampler, null);
        vk.DestroyDescriptorSetLayout(_device.Device, _noiseSetLayout, null);
        vk.DestroyImageView(_device.Device, _noiseView, null);
        vk.DestroyImage(_device.Device, _noiseImage, null);
        vk.FreeMemory(_device.Device, _noiseMemory, null);
    }

    private void BuildSurfaceMatrix()
    {
        if (!_current.Contrast)
        {
            _colorMatrix.CopyTo(_surfaceMatrix, 0);
            return;
        }

        var parameters = _current.ContrastParameters;
        BlurColorMatrix.Build(parameters.Saturation, parameters.Contrast, _surfaceMatrix);
    }

    private void DestroyNoiseImage()
    {
        var vk = _device.Api;
        _ = vk.DeviceWaitIdle(_device.Device);
        vk.DestroyImageView(_device.Device, _noiseView, null);
        vk.DestroyImage(_device.Device, _noiseImage, null);
        vk.FreeMemory(_device.Device, _noiseMemory, null);
        _noiseView = default;
        _noiseImage = default;
        _noiseMemory = default;
    }

    private void CreateNoiseImage()
    {
        var vk = _device.Api;
        _noiseSide = BlurNoise.SizeFor(_options.NoiseScale);
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8Unorm,
            Extent = new Extent3D((uint)_noiseSide, (uint)_noiseSide, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            InitialLayout = ImageLayout.Undefined,
        };
        VulkanDevice.Check(vk.CreateImage(_device.Device, in imageInfo, null, out _noiseImage), "vkCreateImage(blur noise)");
        vk.GetImageMemoryRequirements(_device.Device, _noiseImage, out var requirements);
        var allocate = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = _device.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        VulkanDevice.Check(vk.AllocateMemory(_device.Device, in allocate, null, out _noiseMemory), "vkAllocateMemory(blur noise)");
        VulkanDevice.Check(vk.BindImageMemory(_device.Device, _noiseImage, _noiseMemory, 0), "vkBindImageMemory(blur noise)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _noiseImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        VulkanDevice.Check(vk.CreateImageView(_device.Device, in viewInfo, null, out _noiseView), "vkCreateImageView(blur noise)");

        if (_noiseSet.Handle == 0)
        {
            var setLayout = _noiseSetLayout;
            var allocateSet = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = &setLayout,
            };
            VulkanDevice.Check(vk.AllocateDescriptorSets(_device.Device, in allocateSet, out _noiseSet), "vkAllocateDescriptorSets(blur noise)");
        }
        var descriptorImage = new DescriptorImageInfo(default, _noiseView, ImageLayout.ShaderReadOnlyOptimal);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _noiseSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &descriptorImage,
        };
        vk.UpdateDescriptorSets(_device.Device, 1, in write, 0, null);
    }

    private void UploadNoise()
    {
        var vk = _device.Api;
        var strength = Math.Max(0, _options.NoiseStrength);
        if (_noiseStrength == strength)
        {
            return;
        }

        _noiseStrength = strength;
        var side = _noiseSide;
        var pixels = new byte[side * side];
        BlurNoise.Fill(pixels, strength, _options.NoiseScale);

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = (ulong)pixels.Length,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
        };
        VulkanDevice.Check(vk.CreateBuffer(_device.Device, in bufferInfo, null, out var staging), "vkCreateBuffer(blur noise)");
        vk.GetBufferMemoryRequirements(_device.Device, staging, out var requirements);
        var allocate = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = _device.MemoryTypeFor(
                requirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
        };
        VulkanDevice.Check(vk.AllocateMemory(_device.Device, in allocate, null, out var stagingMemory), "vkAllocateMemory(blur noise)");
        VulkanDevice.Check(vk.BindBufferMemory(_device.Device, staging, stagingMemory, 0), "vkBindBufferMemory(blur noise)");

        void* mapped;
        VulkanDevice.Check(vk.MapMemory(_device.Device, stagingMemory, 0, (ulong)pixels.Length, 0, &mapped), "vkMapMemory(blur noise)");
        pixels.AsSpan().CopyTo(new Span<byte>(mapped, pixels.Length));
        vk.UnmapMemory(_device.Device, stagingMemory);

        var image = _noiseImage;
        _device.SubmitImmediate(commands =>
        {
            var toTransfer = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                SrcAccessMask = AccessFlags.None,
                DstAccessMask = AccessFlags.TransferWriteBit,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            vk.CmdPipelineBarrier(
                commands, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit,
                0, 0, null, 0, null, 1, &toTransfer);

            var copy = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D((uint)side, (uint)side, 1),
            };
            vk.CmdCopyBufferToImage(commands, staging, image, ImageLayout.TransferDstOptimal, 1, in copy);

            var toRead = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.ShaderReadBit,
                OldLayout = ImageLayout.TransferDstOptimal,
                NewLayout = ImageLayout.ShaderReadOnlyOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            vk.CmdPipelineBarrier(
                commands, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit,
                0, 0, null, 0, null, 1, &toRead);
        });

        _ = vk.DeviceWaitIdle(_device.Device);
        vk.DestroyBuffer(_device.Device, staging, null);
        vk.FreeMemory(_device.Device, stagingMemory, null);
    }

    private void DropPyramids()
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
    }
}
