using System.Runtime.InteropServices;

namespace Basin.Rashader;

internal static unsafe class RashaderNative
{
    private const nuint RequiredAbi = 2;
    private const nuint BuiltAgainstApi = 5;

    private static readonly object Gate = new();
    private static bool _probed;
    private static string? _whyNot;

    internal static nuint OptionsVersion { get; private set; }

    internal static delegate* unmanaged[Cdecl]<nuint> libra_instance_abi_version;
    internal static delegate* unmanaged[Cdecl]<nuint> libra_instance_api_version;
    internal static delegate* unmanaged[Cdecl]<nint, byte**, int> libra_error_write;
    internal static delegate* unmanaged[Cdecl]<byte**, int> libra_error_free_string;
    internal static delegate* unmanaged[Cdecl]<nint*, int> libra_error_free;
    internal static delegate* unmanaged[Cdecl]<nint*, nint> libra_preset_ctx_create;
    internal static delegate* unmanaged[Cdecl]<nint*, nint> libra_preset_ctx_free;
    internal static delegate* unmanaged[Cdecl]<nint*, uint, nint> libra_preset_ctx_set_runtime;
    internal static delegate* unmanaged[Cdecl]<nint*, uint, nint> libra_preset_ctx_set_screen_orientation;
    internal static delegate* unmanaged[Cdecl]<nint*, uint, nint> libra_preset_ctx_set_view_aspect_orientation;
    internal static delegate* unmanaged[Cdecl]<byte*, nint*, LibraPresetOptions*, nint*, nint> libra_preset_create_with_options;
    internal static delegate* unmanaged[Cdecl]<nint*, nint> libra_preset_free;
    internal static delegate* unmanaged[Cdecl]<nint*, LibraPresetParamList*, nint> libra_preset_get_runtime_params;
    internal static delegate* unmanaged[Cdecl]<LibraPresetParamList, nint> libra_preset_free_runtime_params;
    internal static delegate* unmanaged[Cdecl]<nint*, LibraDeviceVk, LibraFilterChainVkOptions*, nint*, nint> libra_vk_filter_chain_create;
    internal static delegate* unmanaged[Cdecl]<nint*, nint, nuint, LibraImageVk, LibraImageVk, LibraViewport*, float*, LibraFrameOptions*, nint> libra_vk_filter_chain_frame;
    internal static delegate* unmanaged[Cdecl]<nint*, byte*, float, nint> libra_vk_filter_chain_set_param;
    internal static delegate* unmanaged[Cdecl]<nint*, nint> libra_vk_filter_chain_free;
    internal static delegate* unmanaged[Cdecl]<nint*, nint, LibraFilterChainGlOptions*, nint*, nint> libra_gl_filter_chain_create;
    internal static delegate* unmanaged[Cdecl]<nint*, nuint, LibraImageGl, LibraImageGl, LibraViewport*, float*, LibraFrameOptions*, nint> libra_gl_filter_chain_frame;
    internal static delegate* unmanaged[Cdecl]<nint*, byte*, float, nint> libra_gl_filter_chain_set_param;
    internal static delegate* unmanaged[Cdecl]<nint*, nint> libra_gl_filter_chain_free;

    internal static bool TryLoad(out string? whyNot)
    {
        lock (Gate)
        {
            if (_probed)
            {
                whyNot = _whyNot;
                return _whyNot is null;
            }

            _probed = true;
            _whyNot = Probe();
            whyNot = _whyNot;
            return _whyNot is null;
        }
    }

    private static string? Probe()
    {
        nint library = 0;
        foreach (var name in (ReadOnlySpan<string>)["librashader.so.2", "librashader.so", "librashader"])
        {
            if (NativeLibrary.TryLoad(name, out library))
            {
                break;
            }

            library = 0;
        }

        if (library == 0)
        {
            return "no librashader loads on this host";
        }

        try
        {
            libra_instance_abi_version = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(library, "libra_instance_abi_version");
            libra_instance_api_version = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(library, "libra_instance_api_version");
            libra_error_write = (delegate* unmanaged[Cdecl]<nint, byte**, int>)NativeLibrary.GetExport(library, "libra_error_write");
            libra_error_free_string = (delegate* unmanaged[Cdecl]<byte**, int>)NativeLibrary.GetExport(library, "libra_error_free_string");
            libra_error_free = (delegate* unmanaged[Cdecl]<nint*, int>)NativeLibrary.GetExport(library, "libra_error_free");
            libra_preset_ctx_create = (delegate* unmanaged[Cdecl]<nint*, nint>)NativeLibrary.GetExport(library, "libra_preset_ctx_create");
            libra_preset_ctx_free = (delegate* unmanaged[Cdecl]<nint*, nint>)NativeLibrary.GetExport(library, "libra_preset_ctx_free");
            libra_preset_ctx_set_runtime = (delegate* unmanaged[Cdecl]<nint*, uint, nint>)NativeLibrary.GetExport(library, "libra_preset_ctx_set_runtime");
            libra_preset_ctx_set_screen_orientation = (delegate* unmanaged[Cdecl]<nint*, uint, nint>)NativeLibrary.GetExport(library, "libra_preset_ctx_set_screen_orientation");
            libra_preset_ctx_set_view_aspect_orientation = (delegate* unmanaged[Cdecl]<nint*, uint, nint>)NativeLibrary.GetExport(library, "libra_preset_ctx_set_view_aspect_orientation");
            libra_preset_create_with_options = (delegate* unmanaged[Cdecl]<byte*, nint*, LibraPresetOptions*, nint*, nint>)NativeLibrary.GetExport(library, "libra_preset_create_with_options");
            libra_preset_free = (delegate* unmanaged[Cdecl]<nint*, nint>)NativeLibrary.GetExport(library, "libra_preset_free");
            libra_preset_get_runtime_params = (delegate* unmanaged[Cdecl]<nint*, LibraPresetParamList*, nint>)NativeLibrary.GetExport(library, "libra_preset_get_runtime_params");
            libra_preset_free_runtime_params = (delegate* unmanaged[Cdecl]<LibraPresetParamList, nint>)NativeLibrary.GetExport(library, "libra_preset_free_runtime_params");
            libra_vk_filter_chain_create = (delegate* unmanaged[Cdecl]<nint*, LibraDeviceVk, LibraFilterChainVkOptions*, nint*, nint>)NativeLibrary.GetExport(library, "libra_vk_filter_chain_create");
            libra_vk_filter_chain_frame = (delegate* unmanaged[Cdecl]<nint*, nint, nuint, LibraImageVk, LibraImageVk, LibraViewport*, float*, LibraFrameOptions*, nint>)NativeLibrary.GetExport(library, "libra_vk_filter_chain_frame");
            libra_vk_filter_chain_set_param = (delegate* unmanaged[Cdecl]<nint*, byte*, float, nint>)NativeLibrary.GetExport(library, "libra_vk_filter_chain_set_param");
            libra_vk_filter_chain_free = (delegate* unmanaged[Cdecl]<nint*, nint>)NativeLibrary.GetExport(library, "libra_vk_filter_chain_free");
            libra_gl_filter_chain_create = (delegate* unmanaged[Cdecl]<nint*, nint, LibraFilterChainGlOptions*, nint*, nint>)NativeLibrary.GetExport(library, "libra_gl_filter_chain_create");
            libra_gl_filter_chain_frame = (delegate* unmanaged[Cdecl]<nint*, nuint, LibraImageGl, LibraImageGl, LibraViewport*, float*, LibraFrameOptions*, nint>)NativeLibrary.GetExport(library, "libra_gl_filter_chain_frame");
            libra_gl_filter_chain_set_param = (delegate* unmanaged[Cdecl]<nint*, byte*, float, nint>)NativeLibrary.GetExport(library, "libra_gl_filter_chain_set_param");
            libra_gl_filter_chain_free = (delegate* unmanaged[Cdecl]<nint*, nint>)NativeLibrary.GetExport(library, "libra_gl_filter_chain_free");
        }
        catch (EntryPointNotFoundException missing)
        {
            return $"librashader misses an entry point: {missing.Message}";
        }

        var abi = libra_instance_abi_version();
        if (abi != RequiredAbi)
        {
            return $"librashader speaks ABI {abi}, this build needs ABI {RequiredAbi}";
        }

        OptionsVersion = nuint.Min(libra_instance_api_version(), BuiltAgainstApi);
        return null;
    }
}
