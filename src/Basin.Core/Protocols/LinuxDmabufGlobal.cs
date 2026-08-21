using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Basin.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin;

public sealed class LinuxDmabufGlobal : IDisposable
{
    public const int Version = 6;

    [DllImport("libc", SetLastError = true)]
    private static extern long lseek(int fd, long offset, int whence);

    private readonly WlGlobal _global;
    private readonly ClientBufferRegistry _registry;
    private readonly DrmFormatSet _formats;
    private readonly FdLedger? _ledger;
    private readonly byte[] _table;
    private readonly Dictionary<Wayland.Server.IFdSlotTable, Wayland.Server.Shm.IShmBlob> _tokenFormatTables = [];
    private Wayland.Server.Shm.IShmBlob? _fdFormatTable;
    private readonly (DrmFormat Format, ulong Modifier)[] _tableEntries;
    private readonly byte[] _mainDevice;
    private readonly DrmFormatSet _acceptedFormats;
    private readonly List<(byte[] Device, byte[] Indices)> _extraTranches = [];

    private readonly CompositorGlobal? _compositor;
    private readonly Dictionary<Surface, List<ZwpLinuxDmabufFeedbackV1Resource>> _surfaceFeedbacks = [];
    private readonly Dictionary<Surface, byte[]> _scanoutTranches = [];

    public LinuxDmabufGlobal(
        WlServerDisplay display,
        ClientBufferRegistry registry,
        DrmFormatSet formats,
        string mainDevicePath,
        FdLedger? ledger = null,
        CompositorGlobal? compositor = null,
        IReadOnlyList<(string DevicePath, DrmFormatSet Formats)>? extraDeviceTranches = null)
        : this(display, registry, formats, DeviceId(mainDevicePath), ledger, compositor, extraDeviceTranches)
    {
    }

    public LinuxDmabufGlobal(
        WlServerDisplay display,
        ClientBufferRegistry registry,
        DrmFormatSet formats,
        ulong mainDevice,
        FdLedger? ledger = null,
        CompositorGlobal? compositor = null,
        IReadOnlyList<(string DevicePath, DrmFormatSet Formats)>? extraDeviceTranches = null)
    {
        _registry = registry;
        _formats = formats;
        _ledger = ledger;
        _compositor = compositor;
        _mainDevice = BitConverter.GetBytes(mainDevice);

        var entries = new List<(DrmFormat, ulong)>();
        foreach (var format in formats.Formats)
        {
            foreach (var modifier in formats.ModifiersOf(format))
            {
                entries.Add((format, modifier));
            }
        }

        _acceptedFormats = formats;
        if (extraDeviceTranches is { Count: > 0 })
        {
            _acceptedFormats = new DrmFormatSet();
            foreach (var (format, modifier) in entries)
            {
                _acceptedFormats.Add(format, modifier);
            }

            foreach (var (devicePath, deviceFormats) in extraDeviceTranches)
            {
                var indices = new List<ushort>();
                foreach (var format in deviceFormats.Formats)
                {
                    foreach (var modifier in deviceFormats.ModifiersOf(format))
                    {
                        if (modifier == DrmFormatSet.ModifierInvalid)
                        {
                            continue;
                        }

                        var index = entries.IndexOf((format, modifier));
                        if (index < 0)
                        {
                            index = entries.Count;
                            entries.Add((format, modifier));
                        }

                        indices.Add((ushort)index);
                        _acceptedFormats.Add(format, modifier);
                    }
                }

                if (indices.Count > 0)
                {
                    var bytes = new byte[indices.Count * 2];
                    for (var i = 0; i < indices.Count; i++)
                    {
                        BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), indices[i]);
                    }

                    _extraTranches.Add((BitConverter.GetBytes(DeviceId(devicePath)), bytes));
                }
            }
        }

        _tableEntries = entries.ToArray();
        var table = new byte[_tableEntries.Length * 16];
        for (var i = 0; i < _tableEntries.Length; i++)
        {
            BitConverter.TryWriteBytes(table.AsSpan(i * 16), (uint)_tableEntries[i].Format);
            BitConverter.TryWriteBytes(table.AsSpan(i * 16 + 8), _tableEntries[i].Modifier);
        }

        _table = table;
        _global = display.CreateGlobal(ZwpLinuxDmabufV1.Interface, Version, OnBind);
    }

    public void Dispose()
    {
        _global.Dispose();
        _fdFormatTable?.Dispose();
        foreach (var blob in _tokenFormatTables.Values)
        {
            blob.Dispose();
        }

        _tokenFormatTables.Clear();
    }

    private Wayland.Server.Shm.IShmBlob FormatTableFor(WlClient client)
    {
        if (client.FdSlots is not { } slots)
        {
            return _fdFormatTable ??= Wayland.Server.Shm.ShmBlobs
                .ForFdSlots(null)
                .Create("basin-dmabuf-formats", _table);
        }

        if (!_tokenFormatTables.TryGetValue(slots, out var blob))
        {
            blob = Wayland.Server.Shm.ShmBlobs.ForFdSlots(slots).Create("basin-dmabuf-formats", _table);
            _tokenFormatTables[slots] = blob;
        }

        return blob;
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new ZwpLinuxDmabufV1Resource(client, version, id);

        if (version < 4)
        {
#pragma warning disable CS0618
            foreach (var (format, modifier) in _tableEntries)
            {
                if (version >= 3)
                {
                    resource.SendModifier((uint)format, (uint)(modifier >> 32), (uint)(modifier & 0xFFFFFFFF));
                }
                else if (modifier is DrmFormatSet.ModifierInvalid or DrmFormatSet.ModifierLinear)
                {
                    resource.SendFormat((uint)format);
                }
            }
#pragma warning restore CS0618
        }

        resource.CreateParams += (_, e) =>
        {
            var paramsResource = new ZwpLinuxBufferParamsV1Resource(client, resource.Version, e.ParamsId);
            _ = new BufferParams(this, paramsResource);
        };

        resource.GetDefaultFeedback += (_, e) => SendFeedback(new ZwpLinuxDmabufFeedbackV1Resource(client, resource.Version, e.Id), scanoutIndices: null);
        resource.GetSurfaceFeedback += (_, e) =>
        {
            var feedback = new ZwpLinuxDmabufFeedbackV1Resource(client, resource.Version, e.Id);
            var surface = _compositor?.ResolveSurface(e.Surface);
            if (surface is not null)
            {
                TrackSurfaceFeedback(surface, feedback);
                SendFeedback(feedback, _scanoutTranches.TryGetValue(surface, out var indices) ? indices : null);
            }
            else
            {
                SendFeedback(feedback, scanoutIndices: null);
            }
        };
    }

    public void SetScanoutTargets(Surface surface, DrmFormatSet? scanoutFormats)
    {
        byte[]? indices = null;
        if (scanoutFormats is not null)
        {
            var matched = new List<ushort>();
            for (var i = 0; i < _tableEntries.Length; i++)
            {
                if (scanoutFormats.Contains(_tableEntries[i].Format, _tableEntries[i].Modifier))
                {
                    matched.Add((ushort)i);
                }
            }

            if (matched.Count > 0)
            {
                indices = new byte[matched.Count * 2];
                for (var i = 0; i < matched.Count; i++)
                {
                    BitConverter.TryWriteBytes(indices.AsSpan(i * 2), matched[i]);
                }
            }
        }

        var had = _scanoutTranches.TryGetValue(surface, out var existing);
        if (indices is null && !had)
        {
            return;
        }

        if (indices is not null && had && indices.AsSpan().SequenceEqual(existing))
        {
            return;
        }

        if (indices is null)
        {
            _scanoutTranches.Remove(surface);
        }
        else
        {
            _scanoutTranches[surface] = indices;
        }

        if (_surfaceFeedbacks.TryGetValue(surface, out var resources))
        {
            foreach (var feedback in resources)
            {
                if (!feedback.IsDestroyed)
                {
                    SendFeedback(feedback, indices);
                }
            }
        }
    }

    private void TrackSurfaceFeedback(Surface surface, ZwpLinuxDmabufFeedbackV1Resource feedback)
    {
        if (!_surfaceFeedbacks.TryGetValue(surface, out var list))
        {
            list = [];
            _surfaceFeedbacks[surface] = list;
            surface.Destroyed += () =>
            {
                _surfaceFeedbacks.Remove(surface);
                _scanoutTranches.Remove(surface);
            };
        }

        list.Add(feedback);
        feedback.Destroyed += (_, _) => list.Remove(feedback);
    }

    private void SendFeedback(ZwpLinuxDmabufFeedbackV1Resource feedback, byte[]? scanoutIndices)
    {
        var formatTable = FormatTableFor(feedback.Client);
        feedback.SendFormatTable(formatTable.FdSlot, formatTable.Size);

        if (feedback.Version < 6)
        {
#pragma warning disable CS0618
            feedback.SendMainDevice(_mainDevice);
#pragma warning restore CS0618
        }

        var advertised = (ZwpLinuxDmabufFeedbackV1.TrancheFlags)0;

        if (scanoutIndices is not null)
        {
            var scanoutFlags = ZwpLinuxDmabufFeedbackV1.TrancheFlags.Scanout;
            if (AllSampleable(scanoutIndices))
            {
                scanoutFlags |= ZwpLinuxDmabufFeedbackV1.TrancheFlags.Sampling;
            }

            advertised |= SendTranche(feedback, _mainDevice, scanoutFlags, scanoutIndices);
        }

        var mainCount = 0;
        foreach (var (format, modifier) in _tableEntries)
        {
            if (_formats.Contains(format, modifier))
            {
                mainCount++;
            }
        }

        var indices = new byte[mainCount * 2];
        var written = 0;
        for (var i = 0; i < _tableEntries.Length; i++)
        {
            if (_formats.Contains(_tableEntries[i].Format, _tableEntries[i].Modifier))
            {
                BitConverter.TryWriteBytes(indices.AsSpan(written * 2), (ushort)i);
                written++;
            }
        }

        advertised |= SendTranche(feedback, _mainDevice, ZwpLinuxDmabufFeedbackV1.TrancheFlags.Sampling, indices);

        foreach (var (device, trancheIndices) in _extraTranches)
        {
            advertised |= SendTranche(feedback, device, ZwpLinuxDmabufFeedbackV1.TrancheFlags.Sampling, trancheIndices);
        }

        Debug.Assert(
            (advertised & ZwpLinuxDmabufFeedbackV1.TrancheFlags.Sampling) != 0,
            "feedback advertised no sampling tranche");

        feedback.SendDone();
    }

    private static ZwpLinuxDmabufFeedbackV1.TrancheFlags SendTranche(
        ZwpLinuxDmabufFeedbackV1Resource feedback,
        byte[] device,
        ZwpLinuxDmabufFeedbackV1.TrancheFlags flags,
        byte[] indices)
    {
        feedback.SendTrancheTargetDevice(device);
        feedback.SendTrancheFlags(flags);
        feedback.SendTrancheFormats(indices);
        feedback.SendTrancheDone();
        return flags;
    }

    private bool AllSampleable(byte[] indices)
    {
        for (var i = 0; i + 2 <= indices.Length; i += 2)
        {
            var entry = _tableEntries[BitConverter.ToUInt16(indices, i)];
            if (!_formats.Contains(entry.Format, entry.Modifier))
            {
                return false;
            }
        }

        return true;
    }

    public bool Owns(WlGlobal global) => ReferenceEquals(global, _global);

    internal bool IsSupported(DrmFormat format, ulong modifier) =>
        _acceptedFormats.Contains(format, modifier) ||
        (modifier == DrmFormatSet.ModifierInvalid && _formats.Contains(format));

    internal void RegisterBuffer(WlBufferResource resource, BufferBase buffer)
    {
        _registry.Register(resource.RawHandle, buffer);
        buffer.Released += () =>
        {
            if (!resource.IsDestroyed)
            {
                resource.SendRelease();
            }
        };
        resource.Destroyed += (_, _) => buffer.Destroy();
    }

    internal FdLedger? Ledger => _ledger;

    private static ulong DeviceId(string devicePath) =>
        DrmDevices.TryDeviceId(devicePath, out var devT)
            ? devT
            : throw new InvalidOperationException($"no dev_t for '{devicePath}'");

    private sealed class BufferParams
    {
        private const int PlaneLimit = 4;

        private readonly LinuxDmabufGlobal _owner;
        private readonly ZwpLinuxBufferParamsV1Resource _resource;
        private DmabufPlanes<int> _fds;
        private DmabufPlanes<uint> _offsets;
        private DmabufPlanes<uint> _strides;
        private ulong _modifier;
        private ulong _samplingDevice;
        private int _planeCount;
        private bool _used;
        private IRemoteImage? _image;
        private int _imageSlot = -1;

        internal BufferParams(LinuxDmabufGlobal owner, ZwpLinuxBufferParamsV1Resource resource)
        {
            _owner = owner;
            _resource = resource;
            for (var plane = 0; plane < PlaneLimit; plane++)
            {
                _fds[plane] = -1;
            }

            resource.Add += (_, e) => OnAdd(e);
            resource.SetSamplingDevice += (_, e) => OnSetSamplingDevice(e.Device);
            resource.Create += (_, e) => OnCreate(e.Width, e.Height, (uint)e.Format, immedId: null);
            resource.CreateImmed += (_, e) => OnCreate(e.Width, e.Height, (uint)e.Format, e.BufferId);
            resource.Destroyed += (_, _) => CloseUnclaimedFds();
        }

        private void OnSetSamplingDevice(byte[] device)
        {
            if (device.Length != sizeof(ulong))
            {
                _resource.PostError(
                    (uint)ZwpLinuxBufferParamsV1.Error.InvalidDevTSize,
                    $"a dev_t is {sizeof(ulong)} bytes, not {device.Length}");
                return;
            }

            _samplingDevice = BitConverter.ToUInt64(device);
        }

        private void OnAdd(ZwpLinuxBufferParamsV1Resource.AddEventArgs e)
        {
            if (_resource.Client.FdSlots is { } slots)
            {
                OnAddToken(slots, e);
                return;
            }

            if (_used)
            {
                _owner.Ledger?.AcquiredFromClient(e.Fd, "params add after create");
                _owner.Ledger?.Closed(e.Fd);
                _resource.Client.CloseFd(e.Fd);
                _resource.PostError((uint)ZwpLinuxBufferParamsV1.Error.AlreadyUsed, "params were already used");
                return;
            }

            if (e.PlaneIdx >= PlaneLimit)
            {
                _resource.Client.CloseFd(e.Fd);
                _resource.PostError((uint)ZwpLinuxBufferParamsV1.Error.PlaneIdx, $"plane index {e.PlaneIdx} out of range");
                return;
            }

            var index = (int)e.PlaneIdx;
            if (_fds[index] >= 0)
            {
                _resource.Client.CloseFd(e.Fd);
                _resource.PostError((uint)ZwpLinuxBufferParamsV1.Error.PlaneSet, $"plane {e.PlaneIdx} already set");
                return;
            }

            var modifier = ((ulong)e.ModifierHi << 32) | e.ModifierLo;
            if (_planeCount > 0 && modifier != _modifier)
            {
                _resource.Client.CloseFd(e.Fd);
                _resource.PostError((uint)ZwpLinuxBufferParamsV1.Error.InvalidFormat, "planes disagree on the modifier");
                return;
            }

            _owner.Ledger?.AcquiredFromClient(e.Fd, $"dmabuf params plane {e.PlaneIdx}");
            _fds[index] = e.Fd;
            _offsets[index] = e.Offset;
            _strides[index] = e.Stride;
            _modifier = modifier;
            _planeCount++;
        }

        private void OnAddToken(Wayland.Server.IFdSlotTable slots, ZwpLinuxBufferParamsV1Resource.AddEventArgs e)
        {
            object? payload;
            try
            {
                payload = slots.Resolve<object>(e.Fd);
            }
            catch (WaylandException)
            {
                payload = null;
            }

            if (_used)
            {
                if (payload is not null)
                {
                    _resource.Client.CloseFd(e.Fd);
                }

                _resource.PostError((uint)ZwpLinuxBufferParamsV1.Error.AlreadyUsed, "params were already used");
                return;
            }

            if (payload is not IRemoteImage image)
            {
                if (payload is not null)
                {
                    _resource.Client.CloseFd(e.Fd);
                }

                _resource.PostError(
                    (uint)ZwpLinuxBufferParamsV1.Error.InvalidFormat,
                    "the descriptor token names no host-owned image");
                return;
            }

            if (_image is not null)
            {
                _resource.Client.CloseFd(e.Fd);
                _resource.PostError(
                    (uint)ZwpLinuxBufferParamsV1.Error.InvalidFormat,
                    "a host-owned image is single-plane and these params already hold one");
                return;
            }

            if (e.PlaneIdx != 0)
            {
                _resource.Client.CloseFd(e.Fd);
                _resource.PostError(
                    (uint)ZwpLinuxBufferParamsV1.Error.PlaneIdx,
                    $"a host-owned image is single-plane, so plane index {e.PlaneIdx} names nothing");
                return;
            }

            _image = image;
            _imageSlot = e.Fd;
        }

        private void OnCreate(int width, int height, uint fourcc, uint? immedId)
        {
            if (_used)
            {
                _resource.PostError((uint)ZwpLinuxBufferParamsV1.Error.AlreadyUsed, "params were already used");
                return;
            }

            _used = true;
            var format = (DrmFormat)fourcc;

            if (width <= 0 || height <= 0)
            {
                Fail(immedId, ZwpLinuxBufferParamsV1.Error.InvalidDimensions, $"invalid dimensions {width}x{height}");
                return;
            }

            if (_image is { } image)
            {
                CreateFromImage(image, width, height, format, immedId);
                return;
            }

            if (_planeCount == 0)
            {
                _resource.PostError((uint)ZwpLinuxBufferParamsV1.Error.Incomplete, "no planes were added");
                return;
            }

            for (var plane = 0; plane < _planeCount; plane++)
            {
                if (_fds[plane] < 0)
                {
                    _resource.PostError((uint)ZwpLinuxBufferParamsV1.Error.Incomplete, $"plane {plane} missing");
                    return;
                }
            }

            if (!_owner.IsSupported(format, _modifier))
            {
                Fail(immedId, ZwpLinuxBufferParamsV1.Error.InvalidFormat, $"format {DmabufTestNames.Fourcc(fourcc)} modifier 0x{_modifier:X} not supported");
                return;
            }

            if (_modifier is DrmFormatSet.ModifierLinear or DrmFormatSet.ModifierInvalid)
            {
                for (var plane = 0; plane < _planeCount; plane++)
                {
                    var size = lseek(_fds[plane], 0, 2 );
                    if (size > 0 && _offsets[plane] + (ulong)_strides[plane] * (ulong)height > (ulong)size)
                    {
                        Fail(immedId, ZwpLinuxBufferParamsV1.Error.OutOfBounds, $"plane {plane} exceeds its dmabuf ({size} bytes)");
                        return;
                    }
                }
            }

            var attributes = new DmabufAttributes
            {
                Width = width,
                Height = height,
                Format = format,
                Modifier = _modifier,
                PlaneCount = _planeCount,
                SamplingDevice = _samplingDevice,
            };
            _samplingDevice = 0;
            for (var plane = 0; plane < _planeCount; plane++)
            {
                attributes.Fds[plane] = _fds[plane];
                attributes.Offsets[plane] = _offsets[plane];
                attributes.Strides[plane] = _strides[plane];
                _owner.Ledger?.Transferred(_fds[plane]);
                _fds[plane] = -1;
            }

            var buffer = new DmabufBuffer(attributes, _owner.Ledger, _resource.Client);
            var bufferResource = new WlBufferResource(_resource.Client, 1, immedId ?? 0);
            _owner.RegisterBuffer(bufferResource, buffer);
            if (immedId is null)
            {
                _resource.SendCreated(bufferResource);
            }
        }

        private void CreateFromImage(IRemoteImage image, int width, int height, DrmFormat format, uint? immedId)
        {
            if (width != image.Width || height != image.Height)
            {
                Fail(
                    immedId,
                    ZwpLinuxBufferParamsV1.Error.InvalidDimensions,
                    $"the params claim {width}x{height} and the image is {image.Width}x{image.Height}");
                return;
            }

            if (format != image.Format)
            {
                Fail(
                    immedId,
                    ZwpLinuxBufferParamsV1.Error.InvalidFormat,
                    $"the params claim {DmabufTestNames.Fourcc((uint)format)} and the image is "
                    + DmabufTestNames.Fourcc((uint)image.Format));
                return;
            }

            var buffer = new RemoteImageBuffer(image);
            _resource.Client.CloseFd(_imageSlot);
            _imageSlot = -1;
            _image = null;
            var bufferResource = new WlBufferResource(_resource.Client, 1, immedId ?? 0);
            _owner.RegisterBuffer(bufferResource, buffer);
            if (immedId is null)
            {
                _resource.SendCreated(bufferResource);
            }
        }

        private void Fail(uint? immedId, ZwpLinuxBufferParamsV1.Error error, string message)
        {
            CloseUnclaimedFds();
            if (immedId is not null)
            {
                _resource.PostError((uint)error, message);
            }
            else
            {
                BasinLog.Warn($"dmabuf create rejected: {message}");
                _resource.SendFailed();
            }
        }

        private void CloseUnclaimedFds()
        {
            if (_imageSlot >= 0)
            {
                _resource.Client.CloseFd(_imageSlot);
                _imageSlot = -1;
                _image = null;
            }

            for (var plane = 0; plane < PlaneLimit; plane++)
            {
                if (_fds[plane] >= 0)
                {
                    _resource.Client.CloseFd(_fds[plane]);
                    _owner.Ledger?.Closed(_fds[plane]);
                    _fds[plane] = -1;
                }
            }
        }
    }

}
