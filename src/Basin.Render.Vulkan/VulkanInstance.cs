using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace Basin.Render.Vulkan;

internal sealed unsafe class VulkanInstance : IDisposable
{
    public const string ValidationVariable = "BASIN_VULKAN_VALIDATION";

    private const string ValidationLayer = "VK_LAYER_KHRONOS_validation";
    private const string DebugUtilsExtension = "VK_EXT_debug_utils";

    private const string UnconsumedOutputMessage = "UNASSIGNED-CoreValidation-Shader-OutputNotConsumed";

    public readonly Vk Api;
    public readonly Instance Instance;

    private readonly ExtDebugUtils? _debugUtils;
    private readonly DebugUtilsMessengerEXT _messenger;

    private sealed class VulkanNativeContext : Silk.NET.Core.Contexts.INativeContext
    {
        private readonly nint _library = NativeLibrary.Load("libvulkan.so.1");

        public nint GetProcAddress(string proc, int? slot = null) =>
            NativeLibrary.TryGetExport(_library, proc, out var address) ? address : 0;

        public bool TryGetProcAddress(string proc, out nint address, int? slot = null) =>
            NativeLibrary.TryGetExport(_library, proc, out address);

        public void Dispose()
        {
        }
    }

    public VulkanInstance()
    {
        Api = new Vk(new VulkanNativeContext());
        var validate = Environment.GetEnvironmentVariable(ValidationVariable) is not null;
        var haveDebugUtils = OffersInstanceExtension(DebugUtilsExtension);
        var haveLayer = validate && OffersLayer(ValidationLayer);
        if (validate && !haveLayer)
        {
            BasinLog.Warn($"{ValidationVariable} is set but {ValidationLayer} is not installed; running without it");
        }

        var appName = (byte*)SilkMarshal.StringToPtr("basin");
        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = appName,
            PEngineName = appName,
            ApiVersion = Vk.Version12,
        };

        var extensions = haveDebugUtils ? new[] { DebugUtilsExtension } : [];
        var extensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions);
        var layers = haveLayer ? new[] { ValidationLayer } : [];
        var layerNames = (byte**)SilkMarshal.StringArrayToPtr(layers);

        var messengerInfo = MessengerInfo();
        var instanceInfo = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PNext = validate && haveDebugUtils ? &messengerInfo : null,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = (uint)extensions.Length,
            PpEnabledExtensionNames = extensionNames,
            EnabledLayerCount = (uint)layers.Length,
            PpEnabledLayerNames = layerNames,
        };
        VulkanDevice.Check(Api.CreateInstance(in instanceInfo, null, out Instance), "vkCreateInstance");
        SilkMarshal.Free((nint)appName);
        SilkMarshal.Free((nint)extensionNames);
        SilkMarshal.Free((nint)layerNames);

        if (validate && haveDebugUtils && Api.TryGetInstanceExtension(Instance, out ExtDebugUtils debugUtils))
        {
            _debugUtils = debugUtils;
            var info = MessengerInfo();
            VulkanDevice.Check(
                debugUtils.CreateDebugUtilsMessenger(Instance, in info, null, out _messenger),
                "vkCreateDebugUtilsMessengerEXT");
        }
    }

    private static DebugUtilsMessengerCreateInfoEXT MessengerInfo() => new()
    {
        SType = StructureType.DebugUtilsMessengerCreateInfoExt,
        MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.InfoBitExt |
                          DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                          DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
        MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                      DebugUtilsMessageTypeFlagsEXT.ValidationBitExt |
                      DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
        PfnUserCallback = new PfnDebugUtilsMessengerCallbackEXT(&DebugCallback),
    };

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static Bool32 DebugCallback(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT type,
        DebugUtilsMessengerCallbackDataEXT* data,
        void* userData)
    {
        try
        {
            var id = SilkMarshal.PtrToString((nint)data->PMessageIdName);
            if (id == UnconsumedOutputMessage)
            {
                return Vk.False;
            }

            var message = SilkMarshal.PtrToString((nint)data->PMessage);
            if (BasinLog.Sink is null)
            {
                if ((severity & (DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt | DebugUtilsMessageSeverityFlagsEXT.WarningBitExt)) != 0)
                {
                    Console.Error.WriteLine($"vulkan: {id}: {message}");
                }
            }
            else if ((severity & DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt) != 0)
            {
                BasinLog.Error($"vulkan: {id}: {message}");
            }
            else if ((severity & DebugUtilsMessageSeverityFlagsEXT.WarningBitExt) != 0)
            {
                BasinLog.Warn($"vulkan: {id}: {message}");
            }
            else
            {
                BasinLog.Debug($"vulkan: {id}: {message}");
            }
        }
        catch
        {
        }

        return Vk.False;
    }

    private bool OffersInstanceExtension(string name)
    {
        uint count = 0;
        if (Api.EnumerateInstanceExtensionProperties((byte*)null, ref count, null) != Result.Success)
        {
            return false;
        }

        var properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* propertiesPtr = properties)
        {
            if (Api.EnumerateInstanceExtensionProperties((byte*)null, ref count, propertiesPtr) != Result.Success)
            {
                return false;
            }
        }

        for (var i = 0; i < properties.Length; i++)
        {
            fixed (ExtensionProperties* property = &properties[i])
            {
                if (SilkMarshal.PtrToString((nint)property->ExtensionName) == name)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool OffersLayer(string name)
    {
        uint count = 0;
        if (Api.EnumerateInstanceLayerProperties(ref count, null) != Result.Success)
        {
            return false;
        }

        var layers = new LayerProperties[count];
        fixed (LayerProperties* layersPtr = layers)
        {
            if (Api.EnumerateInstanceLayerProperties(ref count, layersPtr) != Result.Success)
            {
                return false;
            }
        }

        for (var i = 0; i < layers.Length; i++)
        {
            fixed (LayerProperties* layer = &layers[i])
            {
                if (SilkMarshal.PtrToString((nint)layer->LayerName) == name)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_debugUtils is not null)
        {
            _debugUtils.DestroyDebugUtilsMessenger(Instance, _messenger, null);
        }

        Api.DestroyInstance(Instance, null);
        Api.Dispose();
    }
}
