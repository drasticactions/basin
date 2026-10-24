using System.Runtime.InteropServices;
using HexaGen.Runtime;
using Hexa.NET.ImGuizmo;
using Hexa.NET.ImNodes;
using Hexa.NET.ImPlot;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.UI.ImGui;

/// <summary>
/// Loads and checks the four native Dear ImGui libraries: cimgui, cimplot, cimguizmo and cimnodes.
/// </summary>
public static unsafe class ImGuiNative
{
    /// <summary>
    /// The Dear ImGui version that the managed bindings match. Each native library must report this version.
    /// </summary>
    public const string BindingVersion = "1.92.2b";

    private static readonly string[] Libraries = ["cimgui", "cimplot", "cimguizmo", "cimnodes"];

    private static bool _probed;
    private static string? _declined;

    /// <summary>
    /// Gets the Dear ImGui version that the native libraries report. It is <see langword="null"/> before <see cref="TryLoad"/> or when the first library does not load.
    /// </summary>
    public static string? Version { get; private set; }

    /// <summary>
    /// Loads the native libraries and compares their versions with <see cref="BindingVersion"/>. The probe runs once for each process, and later calls return the same result.
    /// </summary>
    /// <param name="declined">The reason that the libraries cannot be used, or <see langword="null"/> when they load.</param>
    /// <returns><see langword="true"/> when all four libraries load and match the bindings.</returns>
    public static bool TryLoad(out string? declined)
    {
        if (!_probed)
        {
            _declined = Probe();
            _probed = true;
            if (_declined is not null)
            {
                ImGuiLog.Log.Info($"native: Dear ImGui unavailable, {_declined}");
            }
        }

        declined = _declined;
        return declined is null;
    }

    private static string? Probe()
    {
        foreach (var library in Libraries)
        {
            nint handle;
            try
            {
                handle = LibraryLoader.LoadLibrary(() => library, null);
            }
            catch (Exception error) when (error is DllNotFoundException or BadImageFormatException
                                              or InvalidOperationException or IOException)
            {
                return $"{library} would not load: {error.Message}";
            }

            if (!NativeLibrary.TryGetExport(handle, "igGetVersion", out var export))
            {
                return $"{library} exports no igGetVersion";
            }

            var version = Marshal.PtrToStringUTF8((nint)((delegate* unmanaged[Cdecl]<byte*>)export)());
            if (version != BindingVersion)
            {
                return $"{library} carries Dear ImGui {version}, and the bindings were generated against {BindingVersion}";
            }

            Version = version;
        }

        try
        {
            _ = ImGuiApi.GetCurrentContext();
            _ = ImPlot.GetCurrentContext();
            _ = ImNodes.GetCurrentContext();
            ImGuizmo.SetImGuiContext(ImGuiApi.GetCurrentContext());
        }
        catch (Exception error) when (error is TypeInitializationException or DllNotFoundException
                                          or EntryPointNotFoundException)
        {
            return $"the Hexa bindings would not bind: {(error.InnerException ?? error).Message}";
        }

        return null;
    }
}
