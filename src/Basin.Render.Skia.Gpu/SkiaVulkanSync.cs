using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Basin.Render.Skia;

internal sealed unsafe class SkiaVulkanSync : IDisposable
{
    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int dup(int fd);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);

    private readonly VulkanDevice _device;
    private readonly KhrExternalSemaphoreFd? _semaphoreFd;
    private readonly Semaphore _signalSemaphore;
    private readonly Fence _drainFence;

    public SkiaVulkanSync(VulkanDevice device)
    {
        _device = device;
        var vk = device.Api;
        if (device.EnabledExtensions.Contains("VK_KHR_external_semaphore_fd") &&
            vk.TryGetDeviceExtension(device.Instance, device.Device, out KhrExternalSemaphoreFd semaphoreFd))
        {
            _semaphoreFd = semaphoreFd;
            var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
            VulkanDevice.Check(vk.CreateSemaphore(device.Device, in semaphoreInfo, null, out _signalSemaphore), "vkCreateSemaphore(signal)");
        }

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        VulkanDevice.Check(vk.CreateFence(device.Device, in fenceInfo, null, out _drainFence), "vkCreateFence(drain)");
    }

    public void WaitFence(int waitFenceFd) => _device.FenceWait.Wait(waitFenceFd);

    public void DrainAndSignal(int signalFenceFd)
    {
        var vk = _device.Api;
        var gpuSignal = signalFenceFd >= 0 && PrepareSignal(signalFenceFd);
        var signalSemaphore = _signalSemaphore;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            SignalSemaphoreCount = gpuSignal ? 1u : 0u,
            PSignalSemaphores = &signalSemaphore,
        };
        var drainFence = _drainFence;
        VulkanDevice.Check(vk.QueueSubmit(_device.Queue, 1, in submit, drainFence), "vkQueueSubmit(drain)");
        VulkanDevice.Check(vk.WaitForFences(_device.Device, 1, in drainFence, true, 10_000_000_000), "vkWaitForFences(drain)");
        VulkanDevice.Check(vk.ResetFences(_device.Device, 1, in drainFence), "vkResetFences(drain)");

        if (signalFenceFd >= 0 && !gpuSignal)
        {
            RenderFences.SignalSyncobjFd(_device.DrmFd, signalFenceFd);
        }
    }

    private bool PrepareSignal(int signalFenceFd)
    {
        if (_semaphoreFd is not { } ext)
        {
            return false;
        }

        var duplicate = dup(signalFenceFd);
        var import = new ImportSemaphoreFdInfoKHR
        {
            SType = StructureType.ImportSemaphoreFDInfoKhr,
            Semaphore = _signalSemaphore,
            HandleType = ExternalSemaphoreHandleTypeFlags.OpaqueFDBit,
            Fd = duplicate,
        };
        if (ext.ImportSemaphoreF(_device.Device, in import) == Result.Success)
        {
            return true;
        }

        _ = close(duplicate);
        return false;
    }

    public void Dispose()
    {
        var vk = _device.Api;
        vk.DestroyFence(_device.Device, _drainFence, null);
        if (_semaphoreFd is not null)
        {
            vk.DestroySemaphore(_device.Device, _signalSemaphore, null);
        }
    }
}
