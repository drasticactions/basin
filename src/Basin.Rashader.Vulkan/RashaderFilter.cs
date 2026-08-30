using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Basin.Diagnostics;
using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using static Basin.Rashader.Vulkan.RashaderVulkanLog;

namespace Basin.Rashader.Vulkan;

public sealed unsafe class RashaderFilter : IVulkanFilter, IRashaderFilter
{
    private static nint _loader;

    private readonly VulkanDevice _device;
    private readonly RashaderParameter[] _parameters;
    private readonly Dictionary<string, byte[]> _parameterNames;
    private nint _chain;
    private bool _disposed;

    private RashaderFilter(VulkanDevice device, nint chain, RashaderParameter[] parameters, bool continuousRepaint)
    {
        _device = device;
        _chain = chain;
        _parameters = parameters;
        _parameterNames = new Dictionary<string, byte[]>(parameters.Length, StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            _parameterNames[parameter.Name] = Encoding.UTF8.GetBytes(parameter.Name + "\0");
        }

        NeedsContinuousRepaint = continuousRepaint;
        BasinCounters.Track();
    }

    public static RashaderFilter? TryCreate(
        VulkanDevice device, string presetPath, in RashaderFilterSettings settings, out string? whyNot)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(presetPath);
        if (!RashaderLibrary.IsAvailable(out whyNot))
        {
            return null;
        }

        if (!TryGetLoader(out var loader))
        {
            whyNot = "no libvulkan with vkGetInstanceProcAddr loads on this host";
            return null;
        }

        var presetOptions = new RashaderPresetOptions
        {
            Runtime = RashaderRuntime.Vulkan,
            VerticalScreen = settings.VerticalScreen,
        };
        using var preset = RashaderPreset.TryCreate(presetPath, presetOptions, out whyNot);
        if (preset is null)
        {
            return null;
        }

        var deviceVk = new LibraDeviceVk
        {
            PhysicalDevice = device.Physical.Handle,
            Instance = device.Instance.Handle,
            Device = device.Device.Handle,
            Queue = device.Queue.Handle,
            Entry = loader,
        };
        var chainOptions = new LibraFilterChainVkOptions
        {
            Version = RashaderNative.OptionsVersion,
            FramesInFlight = (uint)device.FramesInFlight,
            DisableCache = settings.DisableCache ? (byte)1 : (byte)0,
        };
        RashaderParameter[] parameters = [.. preset.Parameters];
        var presetHandle = preset.TakeHandle();
        nint chain = 0;
        var stopwatch = Stopwatch.StartNew();
        whyNot = RashaderError.Consume(
            RashaderNative.libra_vk_filter_chain_create(&presetHandle, deviceVk, &chainOptions, &chain));
        if (whyNot is not null)
        {
            return null;
        }

        Log.Info($"compiled {presetPath} in {stopwatch.ElapsedMilliseconds} ms");
        return new RashaderFilter(device, chain, parameters, settings.ContinuousRepaint);
    }

    public bool IsSupported => _chain != 0;

    public bool NeedsFullFrame => true;

    public bool NeedsContinuousRepaint { get; }

    public IReadOnlyList<RashaderParameter> Parameters => _parameters;

    public bool TrySetParameter(string name, float value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_chain == 0 || !_parameterNames.TryGetValue(name, out var utf8))
        {
            return false;
        }

        var chain = _chain;
        fixed (byte* namePtr = utf8)
        {
            if (RashaderError.Consume(RashaderNative.libra_vk_filter_chain_set_param(&chain, namePtr, value)) is { } message)
            {
                Log.Warn($"set {name}: {message}");
                return false;
            }
        }

        return true;
    }

    public bool Record(in VulkanFilterContext context)
    {
        if (_chain == 0)
        {
            return false;
        }

        var image = new LibraImageVk
        {
            Handle = context.Source.Handle,
            Format = (uint)context.SourceFormat,
            Width = context.SourceExtent.Width,
            Height = context.SourceExtent.Height,
        };
        var output = new LibraImageVk
        {
            Handle = context.Target.Handle,
            Format = (uint)context.TargetFormat,
            Width = context.TargetExtent.Width,
            Height = context.TargetExtent.Height,
        };
        var viewport = new LibraViewport
        {
            X = context.Viewport.X,
            Y = context.Viewport.Y,
            Width = (uint)context.Viewport.Width,
            Height = (uint)context.Viewport.Height,
        };
        var frameOptions = new LibraFrameOptions
        {
            Version = RashaderNative.OptionsVersion,
            FrameDirection = 1,
            Rotation = context.Options.Rotation,
            TotalSubframes = 1,
            CurrentSubframe = 1,
            FramesPerSecond = context.Options.FramesPerSecond,
            FrametimeDelta = context.Options.FrametimeDeltaMillis,
            BrightnessNits = 200f,
        };
        var chain = _chain;
        var error = RashaderNative.libra_vk_filter_chain_frame(
            &chain,
            context.Commands.Handle,
            (nuint)context.Options.FrameCount,
            image,
            output,
            &viewport,
            null,
            &frameOptions);
        if (RashaderError.Consume(error) is { } message)
        {
            Log.Error($"frame failed, the filter is dropped: {message}");
            DropChain();
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        DropChain();
    }

    private void DropChain()
    {
        if (_chain == 0)
        {
            return;
        }

        var chain = _chain;
        _chain = 0;
        _ = _device.Api.DeviceWaitIdle(_device.Device);
        _ = RashaderError.Consume(RashaderNative.libra_vk_filter_chain_free(&chain));
    }

    private static bool TryGetLoader(out nint loader)
    {
        if (_loader != 0)
        {
            loader = _loader;
            return true;
        }

        loader = 0;
        nint library = 0;
        foreach (var name in (ReadOnlySpan<string>)["libvulkan.so.1", "libvulkan.so", "vulkan"])
        {
            if (NativeLibrary.TryLoad(name, out library))
            {
                break;
            }

            library = 0;
        }

        if (library == 0 || !NativeLibrary.TryGetExport(library, "vkGetInstanceProcAddr", out loader))
        {
            return false;
        }

        _loader = loader;
        return true;
    }
}
