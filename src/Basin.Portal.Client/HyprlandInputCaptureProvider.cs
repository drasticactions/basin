using System.Runtime.InteropServices;
using Basin.Eis;
using Basin.Portal;
using Basin.Portal.Client.Protocol;
using Wayland;

namespace Basin.Portal.Client;

public sealed class HyprlandInputCaptureProvider : IInputCaptureProvider, IDisposable
{
    private readonly HyprlandInputCaptureManagerV1 _manager;
    private readonly ClientOutputs _outputs;
    private readonly List<Handle> _handles = [];
    private uint _zoneSet;

    public HyprlandInputCaptureProvider(HyprlandInputCaptureManagerV1 manager, ClientOutputs outputs)
    {
        _manager = manager;
        _outputs = outputs;
        outputs.Changed += OnOutputsChanged;
    }

    public uint ZoneSet => _zoneSet;

    public IInputCaptureHandle? Open(IInputCaptureFront front, string handle)
    {
        var session = _manager.CreateSession(handle);
        var opened = new Handle(this, session, front);
        _handles.Add(opened);
        return opened;
    }

    public void Dispose() => _outputs.Changed -= OnOutputsChanged;

    private void OnOutputsChanged()
    {
        _zoneSet++;
        foreach (var handle in _handles.ToArray())
        {
            handle.Front.ZonesChanged();
        }
    }

    private void Forget(Handle handle) => _handles.Remove(handle);

    private sealed class Handle : IInputCaptureHandle
    {
        private readonly HyprlandInputCaptureProvider _owner;
        private readonly HyprlandInputCaptureV1 _session;
        private int _eisFd = -1;

        public Handle(HyprlandInputCaptureProvider owner, HyprlandInputCaptureV1 session, IInputCaptureFront front)
        {
            _owner = owner;
            _session = session;
            Front = front;
            session.EisFd += (_, e) => _eisFd = e.Fd;
            session.Activated += (_, e) =>
            {
                ActivationId = e.ActivationId;
                Front.Activated(e.ActivationId, e.X.ToDouble(), e.Y.ToDouble(), e.BarrierId);
            };
            session.Deactivated += (_, e) => Front.Deactivated(e.ActivationId);
            session.Disabled += (_, _) => Front.Disabled();
        }

        public IInputCaptureFront Front { get; }

        public uint ActivationId { get; private set; }

        public void ClearBarriers() => _session.ClearBarriers();

        public bool TryAddBarrier(in InputCaptureBarrier barrier)
        {
            _session.AddBarrier(
                _owner._zoneSet,
                barrier.Id,
                unchecked((uint)barrier.X1),
                unchecked((uint)barrier.Y1),
                unchecked((uint)barrier.X2),
                unchecked((uint)barrier.Y2));
            return true;
        }

        public void Enable() => _session.Enable();

        public void Disable() => _session.Disable();

        public bool Release(uint activationId, double x, double y)
        {
            if (activationId != ActivationId)
            {
                return false;
            }

            _session.Release(activationId, WlFixed.FromDouble(x), WlFixed.FromDouble(y));
            return true;
        }

        public int TakeEisFd()
        {
            if (_eisFd < 0)
            {
                return -1;
            }

            var copy = dup(_eisFd);
            return copy;
        }

        public void Dispose()
        {
            if (_eisFd >= 0)
            {
                close(_eisFd);
                _eisFd = -1;
            }

            if (!_session.IsDestroyed)
            {
                _session.Dispose();
            }

            _owner.Forget(this);
        }

        [DllImport("libc")]
        private static extern int dup(int fd);

        [DllImport("libc")]
        private static extern int close(int fd);
    }
}
