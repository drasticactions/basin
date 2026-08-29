using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;

namespace Basin;

public sealed class DmabufBuffer : BufferBase, IBuffer
{
    [DllImport("libc")]
    private static extern int close(int fd);

    private DmabufAttributes _attributes;
    private readonly FdLedger? _ledger;
    private readonly WlClient? _fdOwner;

    public DmabufBuffer(in DmabufAttributes attributes, FdLedger? ledger = null, WlClient? fdOwner = null)
        : base(attributes.Width, attributes.Height)
    {
        _attributes = attributes;
        _ledger = ledger;
        _fdOwner = fdOwner;
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            if (fdOwner is null)
            {
                _ledger?.Acquired(_attributes.Fds[plane], $"dmabuf plane {plane}");
            }
            else
            {
                _ledger?.AcquiredFromClient(_attributes.Fds[plane], $"dmabuf plane {plane}");
            }
        }
    }

    public override DrmFormat Format => _attributes.Format;

    public ulong Modifier => _attributes.Modifier;

    public ulong SamplingDevice => _attributes.SamplingDevice;

    public bool TryGetDmabuf(out DmabufAttributes attributes)
    {
        attributes = _attributes;
        return !IsStorageFreed;
    }

    protected override bool TryMap(BufferDataAccess access, out BufferDataView view)
    {
        view = default;
        return false;
    }

    protected override void OnFreeStorage()
    {
        for (var plane = 0; plane < _attributes.PlaneCount; plane++)
        {
            if (_fdOwner is null)
            {
                FdLedger.AssertNotClientOwned(_attributes.Fds[plane]);
                close(_attributes.Fds[plane]);
            }
            else
            {
                _fdOwner.CloseFd(_attributes.Fds[plane]);
            }

            _ledger?.Closed(_attributes.Fds[plane]);
            _attributes.Fds[plane] = -1;
        }
    }
}
