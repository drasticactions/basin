using Basin.Capabilities;
using Basin.Color;
using Basin.Diagnostics;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using static Basin.Render.Vulkan.VulkanLog;

namespace Basin.Render.Vulkan;

public sealed unsafe class VulkanRenderer : IRenderer
{
    internal readonly struct PipelineGroup(
        Pipeline solid, Pipeline textureIdentity, Pipeline textureSrgb, Pipeline textureLut,
        Pipeline textureIdentityOpaque, Pipeline textureSrgbOpaque, Pipeline textureLutOpaque,
        Pipeline textureColor, Pipeline textureColorOpaque)
    {
        public readonly Pipeline TextureColor = textureColor;

        public readonly Pipeline TextureColorOpaque = textureColorOpaque;

        public readonly Pipeline Solid = solid;

        public readonly Pipeline TextureIdentity = textureIdentity;

        public readonly Pipeline TextureSrgb = textureSrgb;

        public readonly Pipeline TextureLut = textureLut;

        public readonly Pipeline TextureIdentityOpaque = textureIdentityOpaque;

        public readonly Pipeline TextureSrgbOpaque = textureSrgbOpaque;

        public readonly Pipeline TextureLutOpaque = textureLutOpaque;
    }

    internal readonly struct MeshGroup(
        Pipeline overBare, Pipeline overIdentity, Pipeline overSrgb,
        Pipeline addBare, Pipeline addIdentity, Pipeline addSrgb)
    {
        public readonly Pipeline OverBare = overBare;

        public readonly Pipeline OverIdentity = overIdentity;

        public readonly Pipeline OverSrgb = overSrgb;

        public readonly Pipeline AddBare = addBare;

        public readonly Pipeline AddIdentity = addIdentity;

        public readonly Pipeline AddSrgb = addSrgb;

        public Pipeline For(RenderBlend blend, int mode) => blend == RenderBlend.Additive
            ? mode switch { 0 => AddBare, 1 => AddIdentity, _ => AddSrgb }
            : mode switch { 0 => OverBare, 1 => OverIdentity, _ => OverSrgb };
    }

    internal readonly VulkanDevice Dev;

    internal readonly RenderPass OnePass;

    internal readonly RenderPass TwoPass;

    internal readonly PipelineGroup OnePassPipelines;
    internal readonly PipelineGroup TwoPassPipelines;
    internal readonly MeshGroup OnePassMesh;
    internal readonly MeshGroup TwoPassMesh;
    internal readonly Pipeline OutputPipeline;
    internal readonly PipelineLayout Layout;
    internal readonly PipelineLayout LutLayout;
    internal readonly PipelineLayout ColorLayout;
    internal readonly DescriptorSetLayout ColorSetLayout;
    internal readonly VulkanDescriptorPools ColorDescriptors;
    private readonly Dictionary<(ImageDescription Source, ImageDescription Output), VulkanColorTransform?> _colorTransforms =
        new(ImageDescriptionPairComparer.Instance);
    internal readonly PipelineLayout OutputPipeLayout;
    internal readonly DescriptorSetLayout SetLayout;
    internal readonly DescriptorSetLayout LutSetLayout;
    internal readonly DescriptorSetLayout InputSetLayout;
    internal readonly VulkanDescriptorPools Descriptors;
    internal readonly VulkanDescriptorPools InputDescriptors;
    internal readonly Sampler LinearSampler;
    private readonly VulkanRenderPass _pass;
    private readonly Dictionary<IBuffer, (RenderTarget Target, Action Handler)> _targets = [];

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();

    public VulkanRenderer(string drmNodePath)
    {
        Dev = new VulkanDevice(drmNodePath, ["VK_KHR_external_semaphore_fd", "VK_KHR_external_semaphore"]);
        var vk = Dev.Api;
        var device = Dev.Device;

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
        };
        VulkanDevice.Check(vk.CreateSampler(device, in samplerInfo, null, out LinearSampler), "vkCreateSampler");

        var sampler = LinearSampler;
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
        VulkanDevice.Check(vk.CreateDescriptorSetLayout(device, in setLayoutInfo, null, out SetLayout), "vkCreateDescriptorSetLayout");
        VulkanDevice.Check(vk.CreateDescriptorSetLayout(device, in setLayoutInfo, null, out LutSetLayout), "vkCreateDescriptorSetLayout(lut)");

        Descriptors = new VulkanDescriptorPools(Dev);
        InputDescriptors = new VulkanDescriptorPools(Dev, DescriptorType.InputAttachment);

        var inputBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.InputAttachment,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };
        var inputLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &inputBinding,
        };
        VulkanDevice.Check(vk.CreateDescriptorSetLayout(device, in inputLayoutInfo, null, out InputSetLayout), "vkCreateDescriptorSetLayout(input)");

        var pushRange = new PushConstantRange(ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, 112);
        var setLayout = SetLayout;
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        VulkanDevice.Check(vk.CreatePipelineLayout(device, in layoutInfo, null, out Layout), "vkCreatePipelineLayout");

        var lutSetLayouts = stackalloc DescriptorSetLayout[2] { SetLayout, LutSetLayout };
        var lutLayoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 2,
            PSetLayouts = lutSetLayouts,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        VulkanDevice.Check(vk.CreatePipelineLayout(device, in lutLayoutInfo, null, out LutLayout), "vkCreatePipelineLayout(lut)");

        var colorBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };
        var colorSetLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &colorBinding,
        };
        VulkanDevice.Check(vk.CreateDescriptorSetLayout(device, in colorSetLayoutInfo, null, out ColorSetLayout), "vkCreateDescriptorSetLayout(color)");
        ColorDescriptors = new VulkanDescriptorPools(Dev, DescriptorType.UniformBuffer);
        var colorSetLayouts = stackalloc DescriptorSetLayout[2] { SetLayout, ColorSetLayout };
        var colorLayoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 2,
            PSetLayouts = colorSetLayouts,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        VulkanDevice.Check(vk.CreatePipelineLayout(device, in colorLayoutInfo, null, out ColorLayout), "vkCreatePipelineLayout(color)");

        var inputSetLayout = InputSetLayout;
        var outputLayoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &inputSetLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        VulkanDevice.Check(vk.CreatePipelineLayout(device, in outputLayoutInfo, null, out OutputPipeLayout), "vkCreatePipelineLayout(output)");

        OnePass = CreateOnePassRenderPass();
        TwoPass = CreateTwoPassRenderPass();
        var vertex = CreateShaderModule("quad.vert.spv");
        var solidFragment = CreateShaderModule("solid.frag.spv");
        var textureFragment = CreateShaderModule("texture.frag.spv");
        var textureLutFragment = CreateShaderModule("texture_lut.frag.spv");
        var textureColorFragment = CreateShaderModule("texture_color.frag.spv");
        var outputFragment = CreateShaderModule("output.frag.spv");
        OnePassPipelines = BuildGroup(vertex, solidFragment, textureFragment, textureLutFragment, textureColorFragment, OnePass);
        TwoPassPipelines = BuildGroup(vertex, solidFragment, textureFragment, textureLutFragment, textureColorFragment, TwoPass);
        var meshVertex = CreateShaderModule("mesh.vert.spv");
        var meshFragment = CreateShaderModule("mesh.frag.spv");
        OnePassMesh = BuildMeshGroup(meshVertex, meshFragment, OnePass);
        TwoPassMesh = BuildMeshGroup(meshVertex, meshFragment, TwoPass);
        OutputPipeline = CreatePipeline(vertex, outputFragment, OutputPipeLayout, TwoPass, subpass: 1, blend: false, textureTransform: null);
        vk.DestroyShaderModule(device, vertex, null);
        vk.DestroyShaderModule(device, solidFragment, null);
        vk.DestroyShaderModule(device, textureFragment, null);
        vk.DestroyShaderModule(device, textureLutFragment, null);
        vk.DestroyShaderModule(device, outputFragment, null);
        vk.DestroyShaderModule(device, textureColorFragment, null);
        vk.DestroyShaderModule(device, meshVertex, null);
        vk.DestroyShaderModule(device, meshFragment, null);

        _pass = new VulkanRenderPass(this);
    }

    private PipelineGroup BuildGroup(
        ShaderModule vertex, ShaderModule solid, ShaderModule texture, ShaderModule textureLut, ShaderModule textureColor,
        RenderPass renderPass) => new(
        CreatePipeline(vertex, solid, Layout, renderPass, subpass: 0, blend: true, textureTransform: null),
        CreatePipeline(vertex, texture, Layout, renderPass, subpass: 0, blend: true, textureTransform: 0),
        CreatePipeline(vertex, texture, Layout, renderPass, subpass: 0, blend: true, textureTransform: 1),
        CreatePipeline(vertex, textureLut, LutLayout, renderPass, subpass: 0, blend: true, textureTransform: null),
        CreatePipeline(vertex, texture, Layout, renderPass, subpass: 0, blend: false, textureTransform: 0),
        CreatePipeline(vertex, texture, Layout, renderPass, subpass: 0, blend: false, textureTransform: 1),
        CreatePipeline(vertex, textureLut, LutLayout, renderPass, subpass: 0, blend: false, textureTransform: null),
        CreatePipeline(vertex, textureColor, ColorLayout, renderPass, subpass: 0, blend: true, textureTransform: null),
        CreatePipeline(vertex, textureColor, ColorLayout, renderPass, subpass: 0, blend: false, textureTransform: null));

    private MeshGroup BuildMeshGroup(ShaderModule vertex, ShaderModule fragment, RenderPass renderPass) => new(
        CreatePipeline(vertex, fragment, Layout, renderPass, subpass: 0, blend: true, textureTransform: 0, meshInput: true),
        CreatePipeline(vertex, fragment, Layout, renderPass, subpass: 0, blend: true, textureTransform: 1, meshInput: true),
        CreatePipeline(vertex, fragment, Layout, renderPass, subpass: 0, blend: true, textureTransform: 2, meshInput: true),
        CreatePipeline(vertex, fragment, Layout, renderPass, subpass: 0, blend: true, textureTransform: 0, meshInput: true, additive: true),
        CreatePipeline(vertex, fragment, Layout, renderPass, subpass: 0, blend: true, textureTransform: 1, meshInput: true, additive: true),
        CreatePipeline(vertex, fragment, Layout, renderPass, subpass: 0, blend: true, textureTransform: 2, meshInput: true, additive: true));

    public VulkanDevice Device => Dev;

    IRenderDevice? IRenderer.Device => Device;

    public static RenderStack CreateStack(string renderNodePath)
    {
        var renderer = new VulkanRenderer(renderNodePath);
        try
        {
            return new RenderStack(renderer, renderer.Dev.CreateAllocator());
        }
        catch
        {
            renderer.Dispose();
            throw;
        }
    }

    public DrmFormatSet DmabufTextureFormats => Dev.SampleableFormats;

    public ColorTransformCapability ColorTransform => ColorTransformCapability.Decomposed;

    internal VulkanColorTransform? TransformFor(ImageDescription? source, ImageDescription? output)
    {
        source ??= ImageDescription.SdrDefault;
        output ??= ImageDescription.SdrDefault;
        if (ReferenceEquals(source, output))
        {
            return null;
        }

        var key = (source, output);
        if (_colorTransforms.TryGetValue(key, out var cached))
        {
            return cached;
        }

        AllocationScope.Pause();
        try
        {
            var transform = source.IccData is null && !ColorLutBaker.IsIdentity(source, output)
                ? new VulkanColorTransform(this, ColorTransformParameters.From(source, output))
                : null;
            _colorTransforms[key] = transform;
            return transform;
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    public bool SupportsBackdropEffects => true;

    public bool SupportsFrameFilters => true;

    private readonly Dictionary<ulong, DescriptorAllocation> _effectSets = [];

    internal DescriptorSet EffectSetFor(ImageView view)
    {
        if (!_effectSets.TryGetValue(view.Handle, out var allocation))
        {
            allocation = AllocateTextureSet(view);
            _effectSets[view.Handle] = allocation;
        }

        return allocation.Set;
    }

    public bool WaitsOnGpu => Dev.WaitsOnGpu;

    public RenderFencePrecision FencePrecision => RenderFencePrecision.Context;

    private int _completionFence = -1;

    internal void ReplaceCompletionFence(int fd)
    {
        if (_completionFence >= 0)
        {
            Libc.Close(_completionFence);
        }

        _completionFence = fd;
    }

    public int ExportLastSubmissionFence()
    {
        var fd = _completionFence;
        _completionFence = -1;
        return fd;
    }

    public ITexture? ImportTexture(IBuffer buffer)
    {
        _thread.Assert();
        if (buffer.TryGetDmabuf(out var attributes))
        {
            return DmabufTextureFormats.Contains(attributes.Format)
                ? VulkanDmabufTexture.TryImport(this, attributes)
                : null;
        }

        try
        {
            return new VulkanShmTexture(this, buffer);
        }
        catch (InvalidOperationException e)
        {
            Log.Warn($"shm import rejected: {e.Message}");
            return null;
        }
    }

    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        _thread.Assert();
        if (!_targets.TryGetValue(target, out var entry))
        {
            entry = ImportTarget(target);
        }

        _pass.Begin(target, entry.Target, options.WaitFenceFd, options.SignalFenceFd, options.ColorDescription);
        return _pass;
    }

    private (RenderTarget Target, Action Handler) ImportTarget(IBuffer target)
    {
        var created = RenderTarget.Create(this, target);
        void OnDestroyed()
        {
            if (_targets.Remove(target, out var stale))
            {
                stale.Target.Dispose(this);
            }
        }

        var entry = (created, (Action)OnDestroyed);
        _targets[target] = entry;
        target.Destroyed += entry.Item2;
        return entry;
    }

    public void Dispose()
    {
        _thread.Assert();
        var vk = Dev.Api;
        var device = Dev.Device;
        _ = vk.DeviceWaitIdle(device);
        if (_completionFence >= 0)
        {
            Libc.Close(_completionFence);
            _completionFence = -1;
        }

        foreach (var (buffer, entry) in _targets)
        {
            buffer.Destroyed -= entry.Handler;
            entry.Target.Dispose(this);
        }

        _targets.Clear();

        Dev.Ring.ReleaseAllRetired();
        _pass.DestroyResources();
        foreach (var transform in _colorTransforms.Values)
        {
            transform?.Dispose();
        }

        _colorTransforms.Clear();
        foreach (var bundle in _ycbcrSamplers.Values)
        {
            vk.DestroyPipeline(device, bundle.OnePassPipeline, null);
            vk.DestroyPipeline(device, bundle.TwoPassPipeline, null);
            vk.DestroyPipelineLayout(device, bundle.PipelineLayout, null);
            vk.DestroyDescriptorSetLayout(device, bundle.SetLayout, null);
            vk.DestroySampler(device, bundle.Sampler, null);
            vk.DestroySamplerYcbcrConversion(device, bundle.Conversion, null);
        }

        _ycbcrSamplers.Clear();
        DestroyGroup(OnePassPipelines);
        DestroyGroup(TwoPassPipelines);
        DestroyMeshGroup(OnePassMesh);
        DestroyMeshGroup(TwoPassMesh);
        vk.DestroyPipeline(device, OutputPipeline, null);
        vk.DestroyRenderPass(device, OnePass, null);
        vk.DestroyRenderPass(device, TwoPass, null);
        vk.DestroyPipelineLayout(device, Layout, null);
        vk.DestroyPipelineLayout(device, LutLayout, null);
        vk.DestroyPipelineLayout(device, ColorLayout, null);
        ColorDescriptors.Dispose();
        vk.DestroyDescriptorSetLayout(device, ColorSetLayout, null);
        vk.DestroyPipelineLayout(device, OutputPipeLayout, null);
        if (_shaderInfrastructure)
        {
            vk.DestroyPipelineLayout(device, ShaderLayout, null);
            vk.DestroyPipelineLayout(device, ShaderTextureLayout, null);
            _uboDescriptors!.Dispose();
            _uboSets.Clear();
            vk.DestroyDescriptorSetLayout(device, _shaderUboSetLayout, null);
            _shaderInfrastructure = false;
        }
        Descriptors.Dispose();
        InputDescriptors.Dispose();
        vk.DestroyDescriptorSetLayout(device, SetLayout, null);
        vk.DestroyDescriptorSetLayout(device, LutSetLayout, null);
        vk.DestroyDescriptorSetLayout(device, InputSetLayout, null);
        vk.DestroySampler(device, LinearSampler, null);
        Dev.Dispose();
    }

    private void DestroyMeshGroup(in MeshGroup group)
    {
        var vk = Dev.Api;
        vk.DestroyPipeline(Dev.Device, group.OverBare, null);
        vk.DestroyPipeline(Dev.Device, group.OverIdentity, null);
        vk.DestroyPipeline(Dev.Device, group.OverSrgb, null);
        vk.DestroyPipeline(Dev.Device, group.AddBare, null);
        vk.DestroyPipeline(Dev.Device, group.AddIdentity, null);
        vk.DestroyPipeline(Dev.Device, group.AddSrgb, null);
    }

    private void DestroyGroup(in PipelineGroup group)
    {
        var vk = Dev.Api;
        vk.DestroyPipeline(Dev.Device, group.Solid, null);
        vk.DestroyPipeline(Dev.Device, group.TextureIdentity, null);
        vk.DestroyPipeline(Dev.Device, group.TextureSrgb, null);
        vk.DestroyPipeline(Dev.Device, group.TextureLut, null);
        vk.DestroyPipeline(Dev.Device, group.TextureIdentityOpaque, null);
        vk.DestroyPipeline(Dev.Device, group.TextureSrgbOpaque, null);
        vk.DestroyPipeline(Dev.Device, group.TextureLutOpaque, null);
        vk.DestroyPipeline(Dev.Device, group.TextureColor, null);
        vk.DestroyPipeline(Dev.Device, group.TextureColorOpaque, null);
    }

    public IColorLut? ImportLut(ColorLut3D lut)
    {
        _thread.Assert();
        return new VulkanColorLut(this, lut);
    }

    internal const uint ShaderBlockCapacity = 512;

    internal PipelineLayout ShaderLayout;
    internal PipelineLayout ShaderTextureLayout;
    private DescriptorSetLayout _shaderUboSetLayout;
    private VulkanDescriptorPools? _uboDescriptors;
    private readonly Dictionary<ulong, DescriptorAllocation> _uboSets = [];
    private bool _shaderInfrastructure;

    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms)
    {
        _thread.Assert();
        if (source.SpirV.IsEmpty)
        {
            return null;
        }

        EnsureShaderInfrastructure();
        var vk = Dev.Api;
        ShaderModule module;
        fixed (byte* codePtr = source.SpirV.Span)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)source.SpirV.Length,
                PCode = (uint*)codePtr,
            };
            if (vk.CreateShaderModule(Dev.Device, in info, null, out module) != Result.Success)
            {
                throw new InvalidOperationException("vkCreateShaderModule rejected the pixel shader SPIR-V");
            }
        }

        return new VulkanPixelShader(this, module, uniforms, source.SamplesTexture);
    }

    private void EnsureShaderInfrastructure()
    {
        if (_shaderInfrastructure)
        {
            return;
        }

        var vk = Dev.Api;
        var uboBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBufferDynamic,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };
        var uboLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &uboBinding,
        };
        VulkanDevice.Check(vk.CreateDescriptorSetLayout(Dev.Device, in uboLayoutInfo, null, out _shaderUboSetLayout), "vkCreateDescriptorSetLayout(shader)");

        var pushRange = new PushConstantRange(ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, 112);
        var uboSetLayout = _shaderUboSetLayout;
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &uboSetLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        VulkanDevice.Check(vk.CreatePipelineLayout(Dev.Device, in layoutInfo, null, out ShaderLayout), "vkCreatePipelineLayout(shader)");

        var textureSetLayouts = stackalloc DescriptorSetLayout[2] { SetLayout, _shaderUboSetLayout };
        var textureLayoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 2,
            PSetLayouts = textureSetLayouts,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        VulkanDevice.Check(vk.CreatePipelineLayout(Dev.Device, in textureLayoutInfo, null, out ShaderTextureLayout), "vkCreatePipelineLayout(shader texture)");

        _uboDescriptors = new VulkanDescriptorPools(Dev, DescriptorType.UniformBufferDynamic);
        Dev.Staging.BufferDestroyed += buffer =>
        {
            if (_uboSets.Remove(buffer.Handle, out var stale))
            {
                _uboDescriptors.Free(stale);
            }
        };
        _shaderInfrastructure = true;
    }

    internal DescriptorSet UboSetFor(Silk.NET.Vulkan.Buffer buffer)
    {
        if (!_uboSets.TryGetValue(buffer.Handle, out var allocation))
        {
            allocation = _uboDescriptors!.Allocate(_shaderUboSetLayout);
            var bufferInfo = new DescriptorBufferInfo(buffer, 0, ShaderBlockCapacity);
            var write = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = allocation.Set,
                DstBinding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.UniformBufferDynamic,
                PBufferInfo = &bufferInfo,
            };
            Dev.Api.UpdateDescriptorSets(Dev.Device, 1, in write, 0, null);
            _uboSets[buffer.Handle] = allocation;
        }

        return allocation.Set;
    }

    internal Pipeline CreateConsumerPipeline(ShaderModule fragment, bool twoPass, bool samplesTexture, int? textureTransform)
    {
        var vertex = CreateShaderModule("quad.vert.spv");
        var pipeline = CreatePipeline(
            vertex,
            fragment,
            samplesTexture ? ShaderTextureLayout : ShaderLayout,
            twoPass ? TwoPass : OnePass,
            subpass: 0,
            blend: true,
            textureTransform);
        Dev.Api.DestroyShaderModule(Dev.Device, vertex, null);
        return pipeline;
    }

    internal DescriptorAllocation AllocateTextureSet(ImageView view) => AllocateSampledSet(view, SetLayout);

    internal DescriptorAllocation AllocateLutSet(ImageView view) => AllocateSampledSet(view, LutSetLayout);

    private DescriptorAllocation AllocateSampledSet(ImageView view, DescriptorSetLayout setLayout)
    {
        var allocation = Descriptors.Allocate(setLayout);
        var imageInfo = new DescriptorImageInfo(default, view, ImageLayout.General);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = allocation.Set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo,
        };
        Dev.Api.UpdateDescriptorSets(Dev.Device, 1, in write, 0, null);
        return allocation;
    }

    internal sealed class YcbcrSampler
    {
        public SamplerYcbcrConversion Conversion;
        public Sampler Sampler;
        public DescriptorSetLayout SetLayout;
        public PipelineLayout PipelineLayout;
        public Pipeline OnePassPipeline;
        public Pipeline TwoPassPipeline;
    }

    private readonly Dictionary<Format, YcbcrSampler> _ycbcrSamplers = [];

    internal YcbcrSampler GetYcbcrSampler(in VulkanFormatEntry entry)
    {
        if (_ycbcrSamplers.TryGetValue(entry.Vk, out var existing))
        {
            return existing;
        }

        var vk = Dev.Api;
        var device = Dev.Device;
        var bundle = new YcbcrSampler();

        var conversionInfo = new SamplerYcbcrConversionCreateInfo
        {
            SType = StructureType.SamplerYcbcrConversionCreateInfo,
            Format = entry.Vk,
            YcbcrModel = SamplerYcbcrModelConversion.Ycbcr601,
            YcbcrRange = SamplerYcbcrRange.Narrow,
            XChromaOffset = ChromaLocation.Midpoint,
            YChromaOffset = ChromaLocation.Midpoint,
            ChromaFilter = Filter.Linear,
        };
        VulkanDevice.Check(vk.CreateSamplerYcbcrConversion(device, in conversionInfo, null, out bundle.Conversion), "vkCreateSamplerYcbcrConversion");

        var conversionChain = new SamplerYcbcrConversionInfo
        {
            SType = StructureType.SamplerYcbcrConversionInfo,
            Conversion = bundle.Conversion,
        };
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            PNext = &conversionChain,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
        };
        VulkanDevice.Check(vk.CreateSampler(device, in samplerInfo, null, out bundle.Sampler), "vkCreateSampler(ycbcr)");

        var sampler = bundle.Sampler;
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
        VulkanDevice.Check(vk.CreateDescriptorSetLayout(device, in setLayoutInfo, null, out bundle.SetLayout), "vkCreateDescriptorSetLayout(ycbcr)");

        var pushRange = new PushConstantRange(ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, 112);
        var setLayout = bundle.SetLayout;
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        VulkanDevice.Check(vk.CreatePipelineLayout(device, in layoutInfo, null, out bundle.PipelineLayout), "vkCreatePipelineLayout(ycbcr)");

        var vertex = CreateShaderModule("quad.vert.spv");
        var fragment = CreateShaderModule("texture.frag.spv");
        bundle.OnePassPipeline = CreatePipeline(vertex, fragment, bundle.PipelineLayout, OnePass, subpass: 0, blend: true, textureTransform: 1);
        bundle.TwoPassPipeline = CreatePipeline(vertex, fragment, bundle.PipelineLayout, TwoPass, subpass: 0, blend: true, textureTransform: 1);
        vk.DestroyShaderModule(device, vertex, null);
        vk.DestroyShaderModule(device, fragment, null);

        _ycbcrSamplers[entry.Vk] = bundle;
        return bundle;
    }

    internal DescriptorAllocation AllocateYcbcrSet(ImageView view, YcbcrSampler bundle) =>
        AllocateSampledSet(view, bundle.SetLayout);

    internal DescriptorAllocation AllocateInputSet(ImageView view)
    {
        var allocation = InputDescriptors.Allocate(InputSetLayout);
        var imageInfo = new DescriptorImageInfo(default, view, ImageLayout.General);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = allocation.Set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.InputAttachment,
            PImageInfo = &imageInfo,
        };
        Dev.Api.UpdateDescriptorSets(Dev.Device, 1, in write, 0, null);
        return allocation;
    }

    private RenderPass CreateOnePassRenderPass()
    {
        var attachment = new AttachmentDescription
        {
            Format = Format.B8G8R8A8Srgb,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
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
        VulkanDevice.Check(Dev.Api.CreateRenderPass(Dev.Device, in info, null, out var pass), "vkCreateRenderPass(one-pass)");
        return pass;
    }

    private RenderPass CreateTwoPassRenderPass()
    {
        var attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = Format.R16G16B16A16Sfloat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.General,
            FinalLayout = ImageLayout.General,
        };
        attachments[1] = new AttachmentDescription
        {
            Format = Format.B8G8R8A8Unorm,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.General,
            FinalLayout = ImageLayout.General,
        };

        var blendWrite = new AttachmentReference(0, ImageLayout.General);
        var blendRead = new AttachmentReference(0, ImageLayout.General);
        var colorWrite = new AttachmentReference(1, ImageLayout.General);
        var subpasses = stackalloc SubpassDescription[2];
        subpasses[0] = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &blendWrite,
        };
        subpasses[1] = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            InputAttachmentCount = 1,
            PInputAttachments = &blendRead,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorWrite,
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = 1,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.FragmentShaderBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.InputAttachmentReadBit,
            DependencyFlags = DependencyFlags.ByRegionBit,
        };

        var info = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 2,
            PSubpasses = subpasses,
            DependencyCount = 1,
            PDependencies = &dependency,
        };
        VulkanDevice.Check(Dev.Api.CreateRenderPass(Dev.Device, in info, null, out var pass), "vkCreateRenderPass(two-pass)");
        return pass;
    }

    private Pipeline CreatePipeline(
        ShaderModule vertex,
        ShaderModule fragment,
        PipelineLayout layout,
        RenderPass renderPass,
        uint subpass,
        bool blend,
        int? textureTransform,
        bool meshInput = false,
        bool additive = false)
    {
        var entryPoint = (byte*)SilkMarshal.StringToPtr("main");
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

        var specEntry = new SpecializationMapEntry(0, 0, 4);
        var specValue = textureTransform.GetValueOrDefault();
        var specialization = new SpecializationInfo
        {
            MapEntryCount = 1,
            PMapEntries = &specEntry,
            DataSize = 4,
            PData = &specValue,
        };
        if (textureTransform is not null)
        {
            stages[1].PSpecializationInfo = &specialization;
        }

        var vertexInput = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
        var meshBinding = new VertexInputBindingDescription(0, 32, VertexInputRate.Vertex);
        var meshAttributes = stackalloc VertexInputAttributeDescription[3]
        {
            new(0, 0, Format.R32G32Sfloat, 0),
            new(1, 0, Format.R32G32Sfloat, 8),
            new(2, 0, Format.R32G32B32A32Sfloat, 16),
        };
        if (meshInput)
        {
            vertexInput.VertexBindingDescriptionCount = 1;
            vertexInput.PVertexBindingDescriptions = &meshBinding;
            vertexInput.VertexAttributeDescriptionCount = 3;
            vertexInput.PVertexAttributeDescriptions = meshAttributes;
        }

        var inputAssembly = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = meshInput ? PrimitiveTopology.TriangleList : PrimitiveTopology.TriangleStrip,
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
            BlendEnable = blend,
            SrcColorBlendFactor = BlendFactor.One,
            DstColorBlendFactor = additive ? BlendFactor.One : BlendFactor.OneMinusSrcAlpha,
            ColorBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = BlendFactor.One,
            DstAlphaBlendFactor = additive ? BlendFactor.One : BlendFactor.OneMinusSrcAlpha,
            AlphaBlendOp = BlendOp.Add,
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
            Layout = layout,
            RenderPass = renderPass,
            Subpass = subpass,
        };
        VulkanDevice.Check(Dev.Api.CreateGraphicsPipelines(Dev.Device, default, 1, in info, null, out var pipeline), "vkCreateGraphicsPipelines");
        SilkMarshal.Free((nint)entryPoint);
        return pipeline;
    }

    private ShaderModule CreateShaderModule(string resourceName)
    {
        using var stream = typeof(VulkanRenderer).Assembly.GetManifestResourceStream(resourceName)
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
            VulkanDevice.Check(Dev.Api.CreateShaderModule(Dev.Device, in info, null, out var module), $"vkCreateShaderModule({resourceName})");
            return module;
        }
    }
}
