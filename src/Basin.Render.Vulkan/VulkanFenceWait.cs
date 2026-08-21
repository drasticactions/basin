using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Basin.Render.Vulkan;

public sealed unsafe class VulkanFenceWait : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly KhrExternalSemaphoreFd? _semaphoreFd;
    private readonly Semaphore _waitSemaphore;

    public VulkanFenceWait(VulkanDevice device)
    {
        _device = device;
        var vk = device.Api;
        if (device.EnabledExtensions.Contains("VK_KHR_external_semaphore_fd") &&
            vk.TryGetDeviceExtension(device.Instance, device.Device, out KhrExternalSemaphoreFd semaphoreFd))
        {
            _semaphoreFd = semaphoreFd;
            var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
            VulkanDevice.Check(vk.CreateSemaphore(device.Device, in semaphoreInfo, null, out _waitSemaphore), "vkCreateSemaphore(wait)");
        }
    }

    public bool IsGpuSide => _semaphoreFd is not null;

    public void Wait(int syncFileFd)
    {
        if (syncFileFd < 0)
        {
            return;
        }

        if (_semaphoreFd is { } ext)
        {
            var import = new ImportSemaphoreFdInfoKHR
            {
                SType = StructureType.ImportSemaphoreFDInfoKhr,
                Semaphore = _waitSemaphore,
                Flags = SemaphoreImportFlags.TemporaryBit,
                HandleType = ExternalSemaphoreHandleTypeFlags.SyncFDBit,
                Fd = Libc.Dup(syncFileFd),
            };
            if (ext.ImportSemaphoreF(_device.Device, in import) == Result.Success)
            {
                var wait = _waitSemaphore;
                var stage = PipelineStageFlags.AllCommandsBit;
                var submit = new SubmitInfo
                {
                    SType = StructureType.SubmitInfo,
                    WaitSemaphoreCount = 1,
                    PWaitSemaphores = &wait,
                    PWaitDstStageMask = &stage,
                };
                if (_device.Api.QueueSubmit(_device.Queue, 1, in submit, default) == Result.Success)
                {
                    return;
                }
            }
        }

        RenderFences.WaitSyncFile(syncFileFd);
    }

    public void Dispose()
    {
        if (_semaphoreFd is not null)
        {
            _device.Api.DestroySemaphore(_device.Device, _waitSemaphore, null);
        }
    }
}
