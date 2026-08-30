using System.Runtime.InteropServices;
using System.Text;
using Basin.Diagnostics;

namespace Basin.Rashader;

public sealed unsafe class RashaderPreset : IDisposable
{
    private readonly RashaderParameter[] _parameters;
    private nint _handle;
    private bool _disposed;

    private RashaderPreset(nint handle, RashaderParameter[] parameters)
    {
        _handle = handle;
        _parameters = parameters;
        BasinCounters.Track();
    }

    public static RashaderPreset? TryCreate(string path, in RashaderPresetOptions options, out string? whyNot)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!RashaderNative.TryLoad(out whyNot))
        {
            return null;
        }

        nint context = 0;
        whyNot = RashaderError.Consume(RashaderNative.libra_preset_ctx_create(&context));
        if (whyNot is not null)
        {
            return null;
        }

        try
        {
            var orientation = options.VerticalScreen ? 0u : 1u;
            whyNot = RashaderError.Consume(RashaderNative.libra_preset_ctx_set_runtime(&context, (uint)options.Runtime))
                ?? RashaderError.Consume(RashaderNative.libra_preset_ctx_set_screen_orientation(&context, orientation))
                ?? RashaderError.Consume(RashaderNative.libra_preset_ctx_set_view_aspect_orientation(&context, orientation));
            if (whyNot is not null)
            {
                return null;
            }

            var presetOptions = new LibraPresetOptions
            {
                Version = RashaderNative.OptionsVersion,
                OriginalAspectUniforms = 1,
                FrametimeUniforms = 1,
            };
            nint handle = 0;
            var pathBytes = Encoding.UTF8.GetBytes(path + "\0");
            fixed (byte* pathPtr = pathBytes)
            {
                whyNot = RashaderError.Consume(
                    RashaderNative.libra_preset_create_with_options(pathPtr, &context, &presetOptions, &handle));
            }

            if (whyNot is not null)
            {
                return null;
            }

            var parameters = ReadParameters(&handle, out whyNot);
            if (parameters is null)
            {
                _ = RashaderError.Consume(RashaderNative.libra_preset_free(&handle));
                return null;
            }

            return new RashaderPreset(handle, parameters);
        }
        finally
        {
            if (context != 0)
            {
                _ = RashaderError.Consume(RashaderNative.libra_preset_ctx_free(&context));
            }
        }
    }

    public IReadOnlyList<RashaderParameter> Parameters
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _parameters;
        }
    }

    internal nint TakeHandle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var handle = _handle;
        _handle = 0;
        return handle;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        if (_handle != 0)
        {
            var handle = _handle;
            _handle = 0;
            _ = RashaderError.Consume(RashaderNative.libra_preset_free(&handle));
        }
    }

    private static RashaderParameter[]? ReadParameters(nint* handle, out string? whyNot)
    {
        var list = default(LibraPresetParamList);
        whyNot = RashaderError.Consume(RashaderNative.libra_preset_get_runtime_params(handle, &list));
        if (whyNot is not null)
        {
            return null;
        }

        var parameters = new RashaderParameter[(int)list.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var row = list.Parameters[i];
            parameters[i] = new RashaderParameter(
                Marshal.PtrToStringUTF8((nint)row.Name) ?? string.Empty,
                Marshal.PtrToStringUTF8((nint)row.Description) ?? string.Empty,
                row.Initial,
                row.Minimum,
                row.Maximum,
                row.Step);
        }

        _ = RashaderError.Consume(RashaderNative.libra_preset_free_runtime_params(list));
        return parameters;
    }
}
