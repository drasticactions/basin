using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Basin.Render.Vulkan;

internal sealed unsafe class VulkanCommandPool : IDisposable
{
    private const int Slots = 8;

    private struct Slot
    {
        public CommandBuffer Commands;
        public ulong Point;
        public bool Recording;

        public Semaphore Binary;
    }

    private readonly VulkanDevice _device;
    private readonly CommandPool _pool;
    private readonly Slot[] _slots = new Slot[Slots];
    private readonly Semaphore _timeline;
    private readonly Queue<IVulkanRetired> _retiring = new();
    private ulong _point;

    public VulkanCommandPool(VulkanDevice device)
    {
        _device = device;
        var vk = device.Api;
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = device.QueueFamily,
        };
        VulkanDevice.Check(vk.CreateCommandPool(device.Device, in poolInfo, null, out _pool), "vkCreateCommandPool");

        var buffers = stackalloc CommandBuffer[Slots];
        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = Slots,
        };
        VulkanDevice.Check(vk.AllocateCommandBuffers(device.Device, in allocateInfo, buffers), "vkAllocateCommandBuffers");
        for (var i = 0; i < Slots; i++)
        {
            _slots[i].Commands = buffers[i];
        }

        var timelineInfo = new SemaphoreTypeCreateInfo
        {
            SType = StructureType.SemaphoreTypeCreateInfo,
            SemaphoreType = SemaphoreType.Timeline,
            InitialValue = 0,
        };
        var semaphoreInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo,
            PNext = &timelineInfo,
        };
        VulkanDevice.Check(vk.CreateSemaphore(device.Device, in semaphoreInfo, null, out _timeline), "vkCreateSemaphore(timeline)");

        if (device.EnabledExtensions.Contains("VK_KHR_external_semaphore_fd") &&
            vk.TryGetDeviceExtension(device.Instance, device.Device, out Silk.NET.Vulkan.Extensions.KHR.KhrExternalSemaphoreFd fd))
        {
            _semaphoreFd = fd;
            var exportInfo = new ExportSemaphoreCreateInfo
            {
                SType = StructureType.ExportSemaphoreCreateInfo,
                HandleTypes = ExternalSemaphoreHandleTypeFlags.SyncFDBit,
            };
            var binaryInfo = new SemaphoreCreateInfo
            {
                SType = StructureType.SemaphoreCreateInfo,
                PNext = &exportInfo,
            };
            for (var i = 0; i < Slots; i++)
            {
                VulkanDevice.Check(vk.CreateSemaphore(device.Device, in binaryInfo, null, out _slots[i].Binary), "vkCreateSemaphore(binary)");
            }
        }
    }

    private readonly Silk.NET.Vulkan.Extensions.KHR.KhrExternalSemaphoreFd? _semaphoreFd;

    public bool CanExportSyncFile => _semaphoreFd is not null;

    public int ExportSyncFile(CommandBuffer commands)
    {
        if (_semaphoreFd is not { } ext)
        {
            return -1;
        }

        var info = new SemaphoreGetFdInfoKHR
        {
            SType = StructureType.SemaphoreGetFDInfoKhr,
            Semaphore = SlotOf(commands).Binary,
            HandleType = ExternalSemaphoreHandleTypeFlags.SyncFDBit,
        };
        var fd = -1;
        return ext.GetSemaphoreF(_device.Device, &info, &fd) == Result.Success ? fd : -1;
    }

    public Semaphore Timeline => _timeline;

    public ulong CurrentPoint => _point;

    public void Retire(IVulkanRetired resource)
    {
        resource.RetiredAt = _point + (AnyRecording() ? 1ul : 0ul);
        _retiring.Enqueue(resource);
    }

    private void ReleaseRetired(ulong completed)
    {
        while (_retiring.TryPeek(out var next) && next.RetiredAt <= completed)
        {
            _retiring.Dequeue().ReleaseNow();
        }
    }

    private bool AnyRecording()
    {
        for (var i = 0; i < Slots; i++)
        {
            if (_slots[i].Recording)
            {
                return true;
            }
        }

        return false;
    }

    public CommandBuffer Acquire()
    {
        var completed = Completed();
        CompletedPoint = completed;
        ReleaseRetired(completed);
        var oldest = -1;
        for (var i = 0; i < Slots; i++)
        {
            if (_slots[i].Recording)
            {
                continue;
            }

            if (_slots[i].Point <= completed)
            {
                return BeginSlot(i);
            }

            if (oldest < 0 || _slots[i].Point < _slots[oldest].Point)
            {
                oldest = i;
            }
        }

        if (oldest < 0)
        {
            throw new InvalidOperationException("every command buffer slot is recording");
        }

        Wait(_slots[oldest].Point);
        ReleaseRetired(_slots[oldest].Point);
        return BeginSlot(oldest);
    }

    public ulong Submit(CommandBuffer commands)
    {
        var vk = _device.Api;
        VulkanDevice.Check(vk.EndCommandBuffer(commands), "vkEndCommandBuffer");
        var point = ++_point;
        SlotOf(commands).Point = point;
        SlotOf(commands).Recording = false;

        var commandInfo = new CommandBufferSubmitInfo
        {
            SType = StructureType.CommandBufferSubmitInfo,
            CommandBuffer = commands,
        };

        var signalInfos = stackalloc SemaphoreSubmitInfo[2];
        signalInfos[0] = new SemaphoreSubmitInfo
        {
            SType = StructureType.SemaphoreSubmitInfo,
            Semaphore = _timeline,
            Value = point,
            StageMask = PipelineStageFlags2.AllCommandsBit,
        };
        var signalCount = 1u;
        var binary = SlotOf(commands).Binary;
        if (binary.Handle != 0)
        {
            signalInfos[1] = new SemaphoreSubmitInfo
            {
                SType = StructureType.SemaphoreSubmitInfo,
                Semaphore = binary,
                StageMask = PipelineStageFlags2.AllCommandsBit,
            };
            signalCount = 2;
        }

        var submit = new SubmitInfo2
        {
            SType = StructureType.SubmitInfo2,
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = &commandInfo,
            SignalSemaphoreInfoCount = signalCount,
            PSignalSemaphoreInfos = signalInfos,
        };
        VulkanDevice.Check(_device.Synchronization2.QueueSubmit2(_device.Queue, 1, &submit, default), "vkQueueSubmit2");
        return point;
    }

    public bool TrySubmitFrame(CommandBuffer stage, CommandBuffer render, ReadOnlySpan<Semaphore> waits, out ulong renderPoint)
    {
        var vk = _device.Api;
        var hasStage = stage.Handle != 0;
        if (hasStage)
        {
            VulkanDevice.Check(vk.EndCommandBuffer(stage), "vkEndCommandBuffer(stage)");
            SlotOf(stage).Recording = false;
        }

        VulkanDevice.Check(vk.EndCommandBuffer(render), "vkEndCommandBuffer(render)");
        SlotOf(render).Recording = false;

        if (_waitInfos.Length < waits.Length)
        {
            _waitInfos = new SemaphoreSubmitInfo[Math.Max(waits.Length, _waitInfos.Length * 2)];
        }

        for (var i = 0; i < waits.Length; i++)
        {
            _waitInfos[i] = new SemaphoreSubmitInfo
            {
                SType = StructureType.SemaphoreSubmitInfo,
                Semaphore = waits[i],
                StageMask = PipelineStageFlags2.AllCommandsBit,
            };
        }

        var stagePoint = hasStage ? ++_point : 0;
        renderPoint = ++_point;

        var commandInfos = stackalloc CommandBufferSubmitInfo[2];
        var signalInfos = stackalloc SemaphoreSubmitInfo[2];
        var submits = stackalloc SubmitInfo2[2];
        var count = 0u;
        fixed (SemaphoreSubmitInfo* waitPtr = _waitInfos)
        {
            if (hasStage)
            {
                commandInfos[count] = new CommandBufferSubmitInfo { SType = StructureType.CommandBufferSubmitInfo, CommandBuffer = stage };
                signalInfos[count] = new SemaphoreSubmitInfo
                {
                    SType = StructureType.SemaphoreSubmitInfo,
                    Semaphore = _timeline,
                    Value = stagePoint,
                    StageMask = PipelineStageFlags2.AllCommandsBit,
                };
                submits[count] = new SubmitInfo2
                {
                    SType = StructureType.SubmitInfo2,
                    CommandBufferInfoCount = 1,
                    PCommandBufferInfos = commandInfos + count,
                    SignalSemaphoreInfoCount = 1,
                    PSignalSemaphoreInfos = signalInfos + count,
                };
                count++;
            }

            var renderSignals = stackalloc SemaphoreSubmitInfo[2];
            renderSignals[0] = new SemaphoreSubmitInfo
            {
                SType = StructureType.SemaphoreSubmitInfo,
                Semaphore = _timeline,
                Value = renderPoint,
                StageMask = PipelineStageFlags2.AllCommandsBit,
            };
            var renderSignalCount = 1u;
            var binary = SlotOf(render).Binary;
            if (binary.Handle != 0)
            {
                renderSignals[1] = new SemaphoreSubmitInfo
                {
                    SType = StructureType.SemaphoreSubmitInfo,
                    Semaphore = binary,
                    StageMask = PipelineStageFlags2.AllCommandsBit,
                };
                renderSignalCount = 2;
            }

            commandInfos[count] = new CommandBufferSubmitInfo { SType = StructureType.CommandBufferSubmitInfo, CommandBuffer = render };
            submits[count] = new SubmitInfo2
            {
                SType = StructureType.SubmitInfo2,
                WaitSemaphoreInfoCount = (uint)waits.Length,
                PWaitSemaphoreInfos = waitPtr,
                CommandBufferInfoCount = 1,
                PCommandBufferInfos = commandInfos + count,
                SignalSemaphoreInfoCount = renderSignalCount,
                PSignalSemaphoreInfos = renderSignals,
            };
            count++;

            var result = _device.Synchronization2.QueueSubmit2(_device.Queue, count, submits, default);
            if (result == Result.Success)
            {
                if (hasStage)
                {
                    SlotOf(stage).Point = stagePoint;
                }

                SlotOf(render).Point = renderPoint;
                return true;
            }
        }

        if (hasStage)
        {
            Abandon(stage);
        }

        Abandon(render);
        return false;
    }

    public void Abandon(CommandBuffer commands)
    {
        ref var slot = ref SlotOf(commands);
        if (slot.Recording)
        {
            _ = _device.Api.EndCommandBuffer(commands);
        }

        _ = _device.Api.ResetCommandBuffer(commands, 0);
        slot.Recording = false;
        slot.Point = 0;
    }

    private SemaphoreSubmitInfo[] _waitInfos = new SemaphoreSubmitInfo[8];

    public void Wait(ulong point)
    {
        var timeline = _timeline;
        var waitInfo = new SemaphoreWaitInfo
        {
            SType = StructureType.SemaphoreWaitInfo,
            SemaphoreCount = 1,
            PSemaphores = &timeline,
            PValues = &point,
        };
        VulkanDevice.Check(_device.Api.WaitSemaphores(_device.Device, in waitInfo, 10_000_000_000), "vkWaitSemaphores");
    }

    public ulong CompletedPoint { get; private set; }

    public ulong ReadCompleted() => Completed();

    private ulong Completed()
    {
        ulong value;
        VulkanDevice.Check(_device.Api.GetSemaphoreCounterValue(_device.Device, _timeline, &value), "vkGetSemaphoreCounterValue");
        return value;
    }

    private CommandBuffer BeginSlot(int index)
    {
        _slots[index].Recording = true;
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        VulkanDevice.Check(_device.Api.BeginCommandBuffer(_slots[index].Commands, in beginInfo), "vkBeginCommandBuffer");
        return _slots[index].Commands;
    }

    private ref Slot SlotOf(CommandBuffer commands)
    {
        for (var i = 0; i < Slots; i++)
        {
            if (_slots[i].Commands.Handle == commands.Handle)
            {
                return ref _slots[i];
            }
        }

        throw new InvalidOperationException("command buffer does not belong to this pool");
    }

    public void ReleaseAllRetired()
    {
        while (_retiring.TryDequeue(out var resource))
        {
            resource.ReleaseNow();
        }
    }

    public void Dispose()
    {
        var vk = _device.Api;
        ReleaseAllRetired();
        for (var i = 0; i < Slots; i++)
        {
            if (_slots[i].Binary.Handle != 0)
            {
                vk.DestroySemaphore(_device.Device, _slots[i].Binary, null);
            }
        }

        vk.DestroySemaphore(_device.Device, _timeline, null);
        vk.DestroyCommandPool(_device.Device, _pool, null);
    }
}
