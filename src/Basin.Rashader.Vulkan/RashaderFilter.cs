using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Basin.Diagnostics;
using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using static Basin.Rashader.Vulkan.RashaderVulkanLog;

namespace Basin.Rashader.Vulkan;

/// <summary>
/// One librashader filter chain that runs on the Vulkan renderer. It records its passes into the
/// renderer's command buffer.
/// </summary>
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

    /// <summary>
    /// Compiles a preset into a Vulkan filter chain. Every failure returns <see langword="null"/> with
    /// a reason, so a bad preset never ends the session.
    /// </summary>
    /// <param name="device">The Vulkan device whose instance, device and queue the chain uses.</param>
    /// <param name="presetPath">The path of the <c>.slangp</c> preset.</param>
    /// <param name="settings">The settings that the filter reads once at creation.</param>
    /// <param name="whyNot">The reason creation failed, or <see langword="null"/> on success.</param>
    /// <returns>The filter, which the caller owns and must dispose, or <see langword="null"/> when librashader, the loader or the preset fails.</returns>
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

    /// <summary>
    /// Gets a value indicating whether the chain is still live. It becomes <see langword="false"/>
    /// after a failed frame drops the chain, and after disposal.
    /// </summary>
    public bool IsSupported => _chain != 0;

    /// <summary>
    /// Gets a value indicating whether the filter needs the whole frame. It is always <see
    /// langword="true"/>, because a preset can read history and feedback.
    /// </summary>
    public bool NeedsFullFrame => true;

    /// <summary>
    /// Gets a value indicating whether the filter asks for a repaint every frame. It is the <see
    /// cref="RashaderFilterSettings.ContinuousRepaint"/> value from creation.
    /// </summary>
    public bool NeedsContinuousRepaint { get; }

    /// <inheritdoc/>
    public IReadOnlyList<RashaderParameter> Parameters => _parameters;

    /// <inheritdoc/>
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
                Log.Warn($"parameter {name} not set: {message}");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Runs the chain from the context's source into its target. A librashader error logs the message
    /// and drops the chain for good.
    /// </summary>
    /// <param name="context">The source, the target, the viewport and the frame options.</param>
    /// <returns><see langword="true"/> when the chain wrote the target.</returns>
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
            Log.Error($"frame failed, filter dropped: {message}");
            DropChain();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Frees the chain. It waits for the device to idle first, because the chain owns images that the
    /// GPU can still read. A second call does nothing.
    /// </summary>
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
