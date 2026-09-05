using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Drm;
using Drm.Native;
using static Basin.Backend.Drm.DrmLog;

namespace Basin.Backend.Drm;

internal sealed unsafe class DrmFramebufferCache(DrmDevice device) : IDisposable
{
    private readonly Dictionary<IBuffer, Entry> _entries = [];
    private readonly List<(IBuffer Buffer, Entry Entry)> _orphans = [];
    private readonly List<IBuffer> _dead = [];
    private Action? _drop;

    internal Func<IBuffer, bool>? IsScanningOut { get; set; }

    private sealed class Entry
    {
        public uint FbId;
        public uint[] ImportedHandles = [];
        public uint[] PlaneHandles = [];
        public DrmFormat OpaqueFormat;
        public uint OpaqueFbId;
    }

    public uint GetOrAdd(IBuffer buffer) => GetOrAdd(buffer, false);

    public uint GetOrAdd(IBuffer buffer, bool opaque)
    {
        if (!_entries.TryGetValue(buffer, out var entry))
        {
            Basin.Diagnostics.AllocationScope.Pause();
            try
            {
                entry = Import(buffer) ?? new Entry();
                _entries[buffer] = entry;
                _drop ??= DropDestroyed;
                buffer.Destroyed += _drop;
            }
            finally
            {
                Basin.Diagnostics.AllocationScope.Resume();
            }
        }

        if (!opaque || entry.PlaneHandles.Length == 0 || !buffer.TryGetDmabuf(out var attributes))
        {
            return entry.FbId;
        }

        var substitute = attributes.Format.OpaqueSubstitute();
        if (substitute == attributes.Format)
        {
            return entry.FbId;
        }

        if (entry.OpaqueFormat != substitute)
        {
            entry.OpaqueFormat = substitute;
            entry.OpaqueFbId = AddFb(buffer, attributes, substitute, entry.PlaneHandles);
        }

        return entry.OpaqueFbId != 0 ? entry.OpaqueFbId : entry.FbId;
    }

    private uint AddFb(IBuffer buffer, in DmabufAttributes attributes, DrmFormat format, uint[] handles)
    {
        var planeHandles = stackalloc uint[4];
        var planePitches = stackalloc uint[4];
        var planeOffsets = stackalloc uint[4];
        var planeModifiers = stackalloc ulong[4];
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            planeHandles[plane] = handles[plane];
            planePitches[plane] = attributes.Strides[plane];
            planeOffsets[plane] = attributes.Offsets[plane];
            planeModifiers[plane] = attributes.Modifier;
        }

        uint fbId;
        var explicitModifier = attributes.Modifier != DrmFormatSet.ModifierInvalid;
        var result = explicitModifier
            ? Libdrm.drmModeAddFB2WithModifiers(device.Fd, (uint)buffer.Width, (uint)buffer.Height, (uint)format, planeHandles, planePitches, planeOffsets, planeModifiers, &fbId, 2)
            : Libdrm.drmModeAddFB2(device.Fd, (uint)buffer.Width, (uint)buffer.Height, (uint)format, planeHandles, planePitches, planeOffsets, &fbId, 0);
        if (result != 0)
        {
            Log.Debug($"AddFB2 failed for {buffer.Width}x{buffer.Height} {format} modifier 0x{attributes.Modifier:X} planes {attributes.PlaneCount} (errno {Marshal.GetLastPInvokeError()})");
            return 0;
        }

        return fbId;
    }

    private Entry? Import(IBuffer buffer)
    {
        if (buffer is DumbDrmBuffer dumb)
        {
            uint dumbFbId;
            var handles = stackalloc uint[4];
            var pitches = stackalloc uint[4];
            var offsets = stackalloc uint[4];
            handles[0] = dumb.GemHandle;
            pitches[0] = dumb.Stride;
            if (Libdrm.drmModeAddFB2(device.Fd, (uint)buffer.Width, (uint)buffer.Height, (uint)dumb.Format, handles, pitches, offsets, &dumbFbId, 0) != 0)
            {
                return null;
            }

            return new Entry { FbId = dumbFbId };
        }

        if (!buffer.TryGetDmabuf(out var attributes))
        {
            return null;
        }

        var imported = new List<uint>(4);
        var perPlane = new uint[4];
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            uint handle;
            if (Libdrm.drmPrimeFDToHandle(device.Fd, attributes.Fds[plane], &handle) != 0)
            {
                Log.Debug($"prime import failed for {buffer.Width}x{buffer.Height} plane {plane} (errno {Marshal.GetLastPInvokeError()})");
                CloseHandles(imported);
                return null;
            }

            if (!imported.Contains(handle))
            {
                imported.Add(handle);
            }

            perPlane[plane] = handle;
        }

        return new Entry
        {
            FbId = AddFb(buffer, attributes, attributes.Format, perPlane),
            PlaneHandles = perPlane,
            ImportedHandles = imported.ToArray(),
        };
    }

    private void DropDestroyed()
    {
        foreach (var pair in _entries)
        {
            if (pair.Key.IsDestroyed)
            {
                _dead.Add(pair.Key);
            }
        }

        foreach (var buffer in _dead)
        {
            if (_drop is { } drop)
            {
                buffer.Destroyed -= drop;
            }

            Drop(buffer);
        }

        _dead.Clear();
    }

    private void Drop(IBuffer buffer)
    {
        if (!_entries.Remove(buffer, out var entry))
        {
            return;
        }

        if (entry.FbId == 0 && entry.OpaqueFbId == 0 && entry.ImportedHandles.Length == 0)
        {
            return;
        }

        if (IsScanningOut?.Invoke(buffer) == true)
        {
            _orphans.Add((buffer, entry));
            return;
        }

        Release(entry);
    }

    private void Release(Entry entry)
    {
        if (entry.FbId != 0)
        {
            Libdrm.drmModeRmFB(device.Fd, entry.FbId);
        }

        if (entry.OpaqueFbId != 0)
        {
            Libdrm.drmModeRmFB(device.Fd, entry.OpaqueFbId);
        }

        CloseHandles(entry.ImportedHandles);
    }

    internal void ReleaseOrphans()
    {
        if (_orphans.Count == 0)
        {
            return;
        }

        for (var i = _orphans.Count - 1; i >= 0; i--)
        {
            var (buffer, entry) = _orphans[i];
            if (IsScanningOut?.Invoke(buffer) == true)
            {
                continue;
            }

            Release(entry);
            _orphans.RemoveAt(i);
        }
    }

    private void CloseHandles(IReadOnlyList<uint> handles)
    {
        foreach (var handle in handles)
        {
            Libdrm.drmCloseBufferHandle(device.Fd, handle);
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            Release(entry);
        }

        _entries.Clear();
        foreach (var (_, entry) in _orphans)
        {
            Release(entry);
        }

        _orphans.Clear();
    }
}
