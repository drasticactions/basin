using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using static Basin.Render.Vulkan.VulkanLog;

namespace Basin.Render.Vulkan;

internal sealed unsafe class VulkanYuvConverter : IDisposable
{
    private const int MaxTargets = 16;

    private readonly VulkanRenderer _renderer;
    private readonly DescriptorSetLayout _setLayout;
    private readonly PipelineLayout _layout;
    private readonly Pipeline _pipeline;
    private readonly DescriptorPool _pool;
    private readonly Dictionary<IBuffer, Planes> _planes = [];
    private bool _recorded;

    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public float SrcX;
        public float SrcY;
        public float SrcW;
        public float SrcH;
        public float RectX;
        public float RectY;
        public float RectW;
        public float RectH;
        public float Kr;
        public float Kb;
        public float Limited;
        public float Unused;
        public int Width;
        public int Height;
    }

    private sealed class Planes
    {
        public required ImportedDmabuf Luma;
        public required ImportedDmabuf Chroma;
        public DescriptorSet Set;
        public ImageView BoundSource;
        public int Width;
        public int Height;
        public Action? Forget;
    }

    public VulkanYuvConverter(VulkanRenderer renderer, ShaderModule shader)
    {
        _renderer = renderer;
        var device = renderer.Dev;
        var vk = device.Api;
        var bindings = stackalloc DescriptorSetLayoutBinding[3]
        {
            new(0, DescriptorType.CombinedImageSampler, 1, ShaderStageFlags.ComputeBit),
            new(1, DescriptorType.StorageImage, 1, ShaderStageFlags.ComputeBit),
            new(2, DescriptorType.StorageImage, 1, ShaderStageFlags.ComputeBit),
        };
        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = bindings,
        };
        VulkanDevice.Check(vk.CreateDescriptorSetLayout(device.Device, in layoutInfo, null, out _setLayout), "vkCreateDescriptorSetLayout(yuv)");
        var setLayout = _setLayout;
        var range = new PushConstantRange(ShaderStageFlags.ComputeBit, 0, (uint)sizeof(Push));
        var pipelineLayoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &range,
        };
        VulkanDevice.Check(vk.CreatePipelineLayout(device.Device, in pipelineLayoutInfo, null, out _layout), "vkCreatePipelineLayout(yuv)");
        var entry = (byte*)Marshal.StringToHGlobalAnsi("main");
        try
        {
            var pipelineInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit,
                    Module = shader,
                    PName = entry,
                },
                Layout = _layout,
            };
            VulkanDevice.Check(vk.CreateComputePipelines(device.Device, default, 1, in pipelineInfo, null, out _pipeline), "vkCreateComputePipelines(yuv)");
        }
        finally
        {
            Marshal.FreeHGlobal((nint)entry);
        }

        var sizes = stackalloc DescriptorPoolSize[2]
        {
            new(DescriptorType.CombinedImageSampler, MaxTargets),
            new(DescriptorType.StorageImage, MaxTargets * 2),
        };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = MaxTargets,
            PoolSizeCount = 2,
            PPoolSizes = sizes,
        };
        VulkanDevice.Check(vk.CreateDescriptorPool(device.Device, in poolInfo, null, out _pool), "vkCreateDescriptorPool(yuv)");
    }

    public bool Convert(ITexture source, in FBox sourceBox, IBuffer target, YuvCoefficients coefficients, YuvRange range)
    {
        if (PlanesFor(target) is not { } planes)
        {
            return false;
        }

        _recorded = false;
        _renderer.Dev.SubmitImmediate(
            new Job(this, source, planes, sourceBox, coefficients, range),
            static (job, commands) => job.Converter._recorded = job.Converter.Record(commands, job));
        return _recorded;
    }

    private bool Record(CommandBuffer commands, in Job job)
    {
        var device = _renderer.Dev;
        var vk = device.Api;
        var source = job.Source;
        var planes = job.Planes;
        var box = job.Box;
        var coefficients = job.Coefficients;
        var range = job.Range;
        if (!VulkanTextures.TryOpen(device, source, commands, out var view))
        {
            return false;
        }

        if (planes.BoundSource.Handle != view.View.Handle)
        {
            Bind(planes, view.View);
        }

        if (view.Foreign)
        {
            device.AcquireImported(commands, view.Image);
        }

        device.AcquireImported(commands, planes.Luma.Image);
        device.AcquireImported(commands, planes.Chroma.Image);
        var crop = box.IsEmpty ? new FBox(0, 0, view.Width, view.Height) : box;
        var fit = YuvConversion.Fit((int)crop.Width, (int)crop.Height, planes.Width, planes.Height);
        var (kr, kb) = YuvConversion.Weights(coefficients);
        var push = new Push
        {
            SrcX = (float)(crop.X / view.Width),
            SrcY = (float)(crop.Y / view.Height),
            SrcW = (float)(crop.Width / view.Width),
            SrcH = (float)(crop.Height / view.Height),
            RectX = fit.X,
            RectY = fit.Y,
            RectW = fit.Width,
            RectH = fit.Height,
            Kr = kr,
            Kb = kb,
            Limited = range == YuvRange.Limited ? 1f : 0f,
            Width = planes.Width,
            Height = planes.Height,
        };
        vk.CmdBindPipeline(commands, PipelineBindPoint.Compute, _pipeline);
        var set = planes.Set;
        vk.CmdBindDescriptorSets(commands, PipelineBindPoint.Compute, _layout, 0, 1, &set, 0, null);
        vk.CmdPushConstants(commands, _layout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(Push), &push);
        vk.CmdDispatch(commands, (uint)((planes.Width + 15) / 16), (uint)((planes.Height + 15) / 16), 1);
        device.ReleaseImported(commands, planes.Luma.Image);
        device.ReleaseImported(commands, planes.Chroma.Image);
        if (view.Foreign)
        {
            device.ReleaseImported(commands, view.Image);
        }

        return true;
    }

    private readonly record struct Job(
        VulkanYuvConverter Converter, ITexture Source, Planes Planes, FBox Box, YuvCoefficients Coefficients, YuvRange Range);

    private void Bind(Planes planes, ImageView source)
    {
        var device = _renderer.Dev;
        var images = stackalloc DescriptorImageInfo[3]
        {
            new(_renderer.LinearSampler, source, ImageLayout.General),
            new(default, planes.Luma.View, ImageLayout.General),
            new(default, planes.Chroma.View, ImageLayout.General),
        };
        var writes = stackalloc WriteDescriptorSet[3];
        for (var i = 0; i < 3; i++)
        {
            writes[i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = planes.Set,
                DstBinding = (uint)i,
                DescriptorCount = 1,
                DescriptorType = i == 0 ? DescriptorType.CombinedImageSampler : DescriptorType.StorageImage,
                PImageInfo = &images[i],
            };
        }

        device.Api.UpdateDescriptorSets(device.Device, 3, writes, 0, null);
        planes.BoundSource = source;
    }

    private Planes? PlanesFor(IBuffer target)
    {
        if (_planes.TryGetValue(target, out var cached))
        {
            return cached;
        }

        Basin.Diagnostics.AllocationScope.Pause();
        try
        {
            return ImportPlanes(target);
        }
        finally
        {
            Basin.Diagnostics.AllocationScope.Resume();
        }
    }

    private Planes? ImportPlanes(IBuffer target)
    {

        if (_planes.Count >= MaxTargets || !target.TryGetDmabuf(out var attributes) ||
            !YuvConversion.TrySplit(attributes, out var luma, out var chroma))
        {
            return null;
        }

        var device = _renderer.Dev;
        ImportedDmabuf? lumaImage = null;
        try
        {
            lumaImage = VulkanDmabufImport.ImportPlane(device, luma, Format.R8Unorm, ImageUsageFlags.StorageBit);
            var chromaImage = VulkanDmabufImport.ImportPlane(device, chroma, Format.R8G8Unorm, ImageUsageFlags.StorageBit);
            var layout = _setLayout;
            var allocate = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _pool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout,
            };
            VulkanDevice.Check(device.Api.AllocateDescriptorSets(device.Device, in allocate, out var set), "vkAllocateDescriptorSets(yuv)");
            var planes = new Planes { Luma = lumaImage, Chroma = chromaImage, Set = set, Width = attributes.Width, Height = attributes.Height };
            device.SubmitImmediate(commands =>
            {
                device.AcquireImported(commands, planes.Luma.Image);
                device.AcquireImported(commands, planes.Chroma.Image);
                device.ReleaseImported(commands, planes.Luma.Image);
                device.ReleaseImported(commands, planes.Chroma.Image);
            });
            planes.Forget = () => Drop(target);
            target.Destroyed += planes.Forget;
            _planes[target] = planes;
            return planes;
        }
        catch (InvalidOperationException failure)
        {
            if (VulkanLog.FirstRejection(attributes.Format, attributes.Modifier, shm: false))
            {
                Log.Warn(failure, $"NV12 plane import rejected, modifier 0x{attributes.Modifier:X} {attributes.Width}x{attributes.Height}");
            }

            lumaImage?.Destroy(device);
            return null;
        }
    }

    private void Drop(IBuffer target)
    {
        if (_planes.Remove(target, out var planes))
        {
            Release(planes);
        }
    }

    private void Release(Planes planes)
    {
        var device = _renderer.Dev;
        var set = planes.Set;
        _ = device.Api.FreeDescriptorSets(device.Device, _pool, 1, in set);
        planes.Luma.Destroy(device);
        planes.Chroma.Destroy(device);
    }

    public void Dispose()
    {
        var device = _renderer.Dev;
        foreach (var (target, planes) in _planes)
        {
            target.Destroyed -= planes.Forget;
            Release(planes);
        }

        _planes.Clear();
        device.Api.DestroyPipeline(device.Device, _pipeline, null);
        device.Api.DestroyPipelineLayout(device.Device, _layout, null);
        device.Api.DestroyDescriptorPool(device.Device, _pool, null);
        device.Api.DestroyDescriptorSetLayout(device.Device, _setLayout, null);
    }
}
