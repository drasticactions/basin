using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Basin.Diagnostics;
using Basin.Render.Gl;
using static Basin.Rashader.Gl.RashaderGlLog;

namespace Basin.Rashader.Gl;

/// <summary>
/// One librashader filter chain that runs on the GL renderer. Creation, every frame and disposal
/// must run with the device's context current.
/// </summary>
public sealed unsafe class RashaderGlFilter : IGlFilter, IRashaderFilter
{
    private const uint Rgba8 = 0x8058;

    private static nint _loader;

    private readonly RashaderParameter[] _parameters;
    private readonly Dictionary<string, byte[]> _parameterNames;
    private nint _chain;
    private bool _disposed;

    private RashaderGlFilter(nint chain, RashaderParameter[] parameters, bool continuousRepaint)
    {
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
    /// Compiles a preset into a GL filter chain. Every failure returns <see langword="null"/> with a
    /// reason, so a bad preset never ends the session.
    /// </summary>
    /// <param name="device">The GL device. Its context must be current.</param>
    /// <param name="presetPath">The path of the <c>.slangp</c> preset.</param>
    /// <param name="settings">The settings that the filter reads once at creation.</param>
    /// <param name="whyNot">The reason creation failed, or <see langword="null"/> on success.</param>
    /// <returns>The filter, which the caller owns and must dispose, or <see langword="null"/> when librashader, the loader or the preset fails.</returns>
    public static RashaderGlFilter? TryCreate(
        GlDevice device, string presetPath, in RashaderFilterSettings settings, out string? whyNot)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(presetPath);
        if (!RashaderLibrary.IsAvailable(out whyNot))
        {
            return null;
        }

        if (!TryGetLoader(out var loader))
        {
            whyNot = "no libEGL with eglGetProcAddress loads on this host";
            return null;
        }

        var presetOptions = new RashaderPresetOptions
        {
            Runtime = RashaderRuntime.OpenGl,
            VerticalScreen = settings.VerticalScreen,
        };
        using var preset = RashaderPreset.TryCreate(presetPath, presetOptions, out whyNot);
        if (preset is null)
        {
            return null;
        }

        var chainOptions = new LibraFilterChainGlOptions
        {
            Version = RashaderNative.OptionsVersion,
            GlslVersion = 0,
            DisableCache = settings.DisableCache ? (byte)1 : (byte)0,
        };
        RashaderParameter[] parameters = [.. preset.Parameters];
        var presetHandle = preset.TakeHandle();
        nint chain = 0;
        var stopwatch = Stopwatch.StartNew();
        whyNot = RashaderError.Consume(
            RashaderNative.libra_gl_filter_chain_create(&presetHandle, loader, &chainOptions, &chain));
        if (whyNot is not null)
        {
            return null;
        }

        Log.Info($"compiled {presetPath} in {stopwatch.ElapsedMilliseconds} ms");
        return new RashaderGlFilter(chain, parameters, settings.ContinuousRepaint);
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
            if (RashaderError.Consume(RashaderNative.libra_gl_filter_chain_set_param(&chain, namePtr, value)) is { } message)
            {
                Log.Warn($"set {name}: {message}");
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
    public bool Record(in GlFilterContext context)
    {
        if (_chain == 0)
        {
            return false;
        }

        var image = new LibraImageGl
        {
            Handle = context.Source,
            Format = Rgba8,
            Width = (uint)context.SourceWidth,
            Height = (uint)context.SourceHeight,
        };
        var output = new LibraImageGl
        {
            Handle = context.Target,
            Format = Rgba8,
            Width = (uint)context.TargetWidth,
            Height = (uint)context.TargetHeight,
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
        var error = RashaderNative.libra_gl_filter_chain_frame(
            &chain,
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

    /// <summary>
    /// Frees the chain. The device context must be current. A second call does nothing.
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
        _ = RashaderError.Consume(RashaderNative.libra_gl_filter_chain_free(&chain));
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
        foreach (var name in (ReadOnlySpan<string>)["libEGL.so.1", "libEGL.so", "EGL"])
        {
            if (NativeLibrary.TryLoad(name, out library))
            {
                break;
            }

            library = 0;
        }

        if (library == 0 || !NativeLibrary.TryGetExport(library, "eglGetProcAddress", out loader))
        {
            return false;
        }

        _loader = loader;
        return true;
    }
}
