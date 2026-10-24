using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Basin.Render.Vulkan;

/// <summary>
/// Makes the queue of a <see cref="VulkanDevice"/> wait for sync_file fences. It falls back to a CPU wait when the device cannot import one.
/// </summary>
public sealed unsafe class VulkanFenceWait : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly KhrExternalSemaphoreFd? _semaphoreFd;
    private readonly List<(Semaphore Semaphore, Fence Fence)> _slots = new(4);

    /// <summary>
    /// Initializes a new instance of the <see cref="VulkanFenceWait"/> class.
    /// </summary>
    /// <param name="device">The device whose queue waits.</param>
    public VulkanFenceWait(VulkanDevice device)
    {
        _device = device;
        var vk = device.Api;
        if (device.EnabledExtensions.Contains("VK_KHR_external_semaphore_fd") &&
            vk.TryGetDeviceExtension(device.Instance, device.Device, out KhrExternalSemaphoreFd semaphoreFd))
        {
            _semaphoreFd = semaphoreFd;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the device imports sync_file fences, so that <see cref="Wait"/> does not block the CPU.
    /// </summary>
    public bool IsGpuSide => _semaphoreFd is not null;

    /// <summary>
    /// Gets the number of semaphore and fence pairs that the object made. Finished pairs are used again.
    /// </summary>
    public int Slots => _slots.Count;

    /// <summary>
    /// Makes later submissions on the queue wait for a fence. When the GPU wait fails, it blocks the calling thread until the fence signals.
    /// </summary>
    /// <param name="syncFileFd">
    /// A sync_file fd that the caller keeps, or a negative value for no wait. The method keeps a duplicate only through a successful import.
    /// </param>
    public void Wait(int syncFileFd)
    {
        if (syncFileFd < 0)
        {
            return;
        }

        if (_semaphoreFd is { } ext && TryGpuWait(ext, syncFileFd))
        {
            return;
        }

        RenderFences.WaitSyncFile(syncFileFd);
    }

    private bool TryGpuWait(KhrExternalSemaphoreFd ext, int syncFileFd)
    {
        var duplicate = Libc.Dup(syncFileFd);
        if (duplicate < 0)
        {
            return false;
        }

        var slot = FreeSlot();
        var (semaphore, fence) = _slots[slot];
        var import = new ImportSemaphoreFdInfoKHR
        {
            SType = StructureType.ImportSemaphoreFDInfoKhr,
            Semaphore = semaphore,
            Flags = SemaphoreImportFlags.TemporaryBit,
            HandleType = ExternalSemaphoreHandleTypeFlags.SyncFDBit,
            Fd = duplicate,
        };
        if (ext.ImportSemaphoreF(_device.Device, in import) != Result.Success)
        {
            RenderFences.CloseFence(duplicate);
            return false;
        }

        var stage = PipelineStageFlags.AllCommandsBit;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &semaphore,
            PWaitDstStageMask = &stage,
        };
        _ = _device.Api.ResetFences(_device.Device, 1, &fence);
        if (_device.Api.QueueSubmit(_device.Queue, 1, in submit, fence) == Result.Success)
        {
            return true;
        }

        _device.Api.DestroyFence(_device.Device, fence, null);
        _device.Api.DestroySemaphore(_device.Device, semaphore, null);
        _slots.RemoveAt(slot);
        return false;
    }

    /// <summary>
    /// Waits for each pending wait submission, then destroys every semaphore and fence.
    /// A slot whose submit failed is destroyed at once, so the wait never blocks on a fence that nothing will signal.
    /// </summary>
    public void Dispose()
    {
        foreach (var (semaphore, fence) in _slots)
        {
            var handle = fence;
            _ = _device.Api.WaitForFences(_device.Device, 1, &handle, true, ulong.MaxValue);
            _device.Api.DestroyFence(_device.Device, fence, null);
            _device.Api.DestroySemaphore(_device.Device, semaphore, null);
        }

        _slots.Clear();
    }

    private int FreeSlot()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            if (_device.Api.GetFenceStatus(_device.Device, _slots[i].Fence) == Result.Success)
            {
                return i;
            }
        }

        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        VulkanDevice.Check(_device.Api.CreateSemaphore(_device.Device, in semaphoreInfo, null, out var semaphore), "vkCreateSemaphore(wait)");
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
        VulkanDevice.Check(_device.Api.CreateFence(_device.Device, in fenceInfo, null, out var fence), "vkCreateFence(wait)");
        _slots.Add((semaphore, fence));
        return _slots.Count - 1;
    }
}
