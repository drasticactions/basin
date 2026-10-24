using System.Runtime.InteropServices;
using SDL3;
using Silk.NET.Core.Contexts;

namespace Basin.Sdl;

internal sealed unsafe class SdlVulkanNativeContext : INativeContext
{
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, nint> _getInstanceProcAddr;

    public SdlVulkanNativeContext()
    {
        var address = SDL.VulkanGetVkGetInstanceProcAddr();
        if (address == 0)
        {
            throw new InvalidOperationException($"SDL has no Vulkan loader. SDL reports \"{SDL.GetError()}\".");
        }

        _getInstanceProcAddr = (delegate* unmanaged[Cdecl]<nint, byte*, nint>)address;
    }

    public nint Instance { get; set; }

    public nint GetProcAddress(string proc, int? slot = null)
    {
        var name = Marshal.StringToCoTaskMemUTF8(proc);
        try
        {
            var found = _getInstanceProcAddr(Instance, (byte*)name);
            return found != 0 || Instance == 0 ? found : _getInstanceProcAddr(0, (byte*)name);
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }

    public bool TryGetProcAddress(string proc, out nint address, int? slot = null)
    {
        address = GetProcAddress(proc, slot);
        return address != 0;
    }

    public void Dispose()
    {
    }
}
