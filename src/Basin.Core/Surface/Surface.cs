using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

public sealed class Surface
{
    private const CommitParkReason DeferredReasons =
        CommitParkReason.FifoBarrier | CommitParkReason.CommitTiming | CommitParkReason.Held;

    private readonly CompositorGlobal _owner;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private SurfaceState? _cached;
    private SurfaceCommitQueue? _queue;
    private bool _pendingArmBarrier;
    private bool _pendingWaitBarrier;
    private long _pendingCommitTimeNanos;
    private bool _barrierArmed;
    private bool _pendingHold;
    private bool _holdCleared;
    private bool _destroyed;

    internal Surface(CompositorGlobal owner, WlSurfaceResource resource)
    {
        _owner = owner;
        Resource = resource;
        BasinCounters.Track();

        resource.Attach += (_, e) => OnAttach(e);
        resource.Damage += (_, e) => OnDamage(e.X, e.Y, e.Width, e.Height, Pending.SurfaceDamage, SurfaceStateFields.SurfaceDamage);
        resource.DamageBuffer += (_, e) => OnDamage(e.X, e.Y, e.Width, e.Height, Pending.BufferDamage, SurfaceStateFields.BufferDamage);
        resource.Frame += (_, e) => OnFrame(e.Callback);
        resource.GetRelease += (_, e) => OnGetRelease(e.Callback);
        resource.SetOpaqueRegion += (_, e) => OnSetOpaqueRegion(e.Region);
        resource.SetInputRegion += (_, e) => OnSetInputRegion(e.Region);
        resource.SetBufferTransform += (_, e) => OnSetBufferTransform((int)e.Transform);
        resource.SetBufferScale += (_, e) => OnSetBufferScale(e.Scale);
        resource.Offset += (_, e) => OnOffset(e.X, e.Y);
        resource.Commit += (_, _) => OnCommit();
        resource.Destroyed += (_, _) => OnDestroyed();
    }

    public WlSurfaceResource Resource { get; }

    public SurfaceState Current { get; } = new();

    public SurfaceState Pending { get; } = new();

    public string? Role { get; private set; }

    public object? RoleObject { get; private set; }

    public Subsurface? SubsurfaceRole { get; internal set; }

    public List<Subsurface> SubsurfacesBelow { get; } = [];

    public List<Subsurface> SubsurfacesAbove { get; } = [];

    public bool IsMapped => Current.Buffer is not null && !_destroyed;

    public bool IsDestroyed => _destroyed;

    public event Action? Committed;

    public event Action? CommitRequested;

    public event Action? Destroyed;

    public bool CanSetRole(string role) => Role is null || (Role == role && RoleObject is null);

    public bool TrySetRole(string role, object roleObject)
    {
        if (!CanSetRole(role))
        {
            return false;
        }

        Role = role;
        RoleObject = roleObject;
        return true;
    }

    public void ClearRoleObject()
    {
        RoleObject = null;
        SubsurfaceRole = null;
    }

    public void SendFrameDone(uint timestampMs)
    {
        foreach (var resource in Current.FrameResources)
        {
            if (!resource.IsDestroyed)
            {
                resource.SendDone(timestampMs);
                resource.Destroy();
            }
        }

        Current.FrameResources.Clear();

        if (Current.FrameCallbacks.Count == 0)
        {
            return;
        }

        foreach (var callback in Current.FrameCallbacks)
        {
            callback.Done(timestampMs);
        }

        Current.FrameCallbacks.Clear();
    }

    private HashSet<OutputGlobal>? _enteredOutputs;

    public void SetOutputPresence(OutputGlobal output, bool inside)
    {
        if (_destroyed || inside == (_enteredOutputs?.Contains(output) ?? false))
        {
            return;
        }

        if (inside)
        {
            (_enteredOutputs ??= []).Add(output);
        }
        else
        {
            _enteredOutputs?.Remove(output);
        }

        foreach (var resource in output.ResourcesOf(Resource.Client))
        {
            if (inside)
            {
                Resource.SendEnter(resource);
            }
            else
            {
                Resource.SendLeave(resource);
            }
        }

        OutputPresenceChanged?.Invoke();
    }

    public IReadOnlyCollection<OutputGlobal> EnteredOutputs =>
        _enteredOutputs ?? (IReadOnlyCollection<OutputGlobal>)Array.Empty<OutputGlobal>();

    public event Action? OutputPresenceChanged;

    public double EnteredOutputScale
    {
        get
        {
            var scale = 0.0;
            if (_enteredOutputs is { } entered)
            {
                foreach (var output in entered)
                {
                    scale = Math.Max(scale, output.Output.Scale);
                }
            }

            return scale;
        }
    }

    private int _preferredBufferScale;

    public void SetPreferredBufferScale(int scale)
    {
        if (_destroyed || scale == _preferredBufferScale || Resource.Version < 6)
        {
            return;
        }

        _preferredBufferScale = scale;
        Resource.SendPreferredBufferScale(scale);
    }

    private void OnAttach(WlSurfaceResource.AttachEventArgs e)
    {
        if (Resource.Version >= 5 && (e.X != 0 || e.Y != 0))
        {
            Resource.PostError((uint)WlSurface.Error.InvalidOffset, "wl_surface.attach with non-zero offset requires wl_surface.offset");
            return;
        }

        var buffer = _owner.Buffers.GetOrImport(e.BufferHandle);
        if (e.BufferHandle != 0 && buffer is null)
        {
            Resource.PostError(0, "unsupported buffer type");
            return;
        }

        Pending.SetBuffer(buffer);
        Pending.Committed |= SurfaceStateFields.Buffer;
        if (Resource.Version < 5 && (e.X != 0 || e.Y != 0))
        {
            Pending.OffsetX = e.X;
            Pending.OffsetY = e.Y;
            Pending.Committed |= SurfaceStateFields.Offset;
        }
    }

    private void OnDamage(int x, int y, int width, int height, Pixman.PixmanRegion32 target, SurfaceStateFields field)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        width = Math.Min(width, int.MaxValue - Math.Max(0, x));
        height = Math.Min(height, int.MaxValue - Math.Max(0, y));
        target.UnionRect(target, x, y, (uint)width, (uint)height);
        if (field == SurfaceStateFields.SurfaceDamage)
        {
            Pending.SurfaceDamageRects.Add(x, y, width, height);
        }
        else
        {
            Pending.BufferDamageRects.Add(x, y, width, height);
        }

        Pending.Committed |= field;
    }

    private void OnFrame(uint callbackId)
    {
        var resource = new WlCallbackResource(Resource.Client, 1, callbackId);
        Pending.FrameResources.Add(resource);
        Pending.Committed |= SurfaceStateFields.FrameCallbacks;
    }

    private void OnGetRelease(uint callbackId)
    {
        var resource = new WlCallbackResource(Resource.Client, 1, callbackId);

        Pending.BufferRelease?.Cancel();
        Pending.BufferRelease = new FrameCallback(resource, timestamped: false);
        Pending.Committed |= SurfaceStateFields.BufferRelease;
    }

    private void OnSetOpaqueRegion(WlRegionResource? regionResource)
    {
        if (Pending.HasOpaque)
        {
            Pending.Opaque.Clear();
        }

        if (_owner.ResolveRegion(regionResource) is { } region)
        {
            Pending.Opaque.Copy(region.Pixman);
        }

        Pending.Committed |= SurfaceStateFields.OpaqueRegion;
    }

    private void OnSetInputRegion(WlRegionResource? regionResource)
    {
        if (Pending.HasInput)
        {
            Pending.Input.Clear();
        }

        if (_owner.ResolveRegion(regionResource) is { } region)
        {
            Pending.Input.Copy(region.Pixman);
            Pending.InputIsInfinite = false;
        }
        else
        {
            Pending.InputIsInfinite = true;
        }

        Pending.Committed |= SurfaceStateFields.InputRegion;
    }

    private void OnSetBufferTransform(int transform)
    {
        if (transform is < 0 or > 7)
        {
            Resource.PostError((uint)WlSurface.Error.InvalidTransform, $"invalid transform {transform}");
            return;
        }

        Pending.Transform = (OutputTransform)transform;
        Pending.Committed |= SurfaceStateFields.Transform;
    }

    private void OnSetBufferScale(int scale)
    {
        if (scale <= 0)
        {
            Resource.PostError((uint)WlSurface.Error.InvalidScale, $"invalid scale {scale}");
            return;
        }

        Pending.Scale = scale;
        Pending.Committed |= SurfaceStateFields.Scale;
    }

    private void OnOffset(int x, int y)
    {
        Pending.OffsetX = x;
        Pending.OffsetY = y;
        Pending.Committed |= SurfaceStateFields.Offset;
    }

    public bool HasParkedCommits => _queue is { IsEmpty: false };

    public long NextParkedCommitTimeNanos => _queue?.EarliestTargetTimeNanos ?? 0;

    public void SetFifoBarrier() => _pendingArmBarrier = true;

    public void WaitFifoBarrier() => _pendingWaitBarrier = true;

    public bool FifoBarrierArmed => _barrierArmed;

    public void ClearFifoBarrier() => _barrierArmed = false;

    public bool HasPendingCommitTime => _pendingCommitTimeNanos > 0;

    public void SetCommitTime(long targetNanos) => _pendingCommitTimeNanos = targetNanos;

    public void HoldNextCommit() => _pendingHold = true;

    public bool ReleaseHeldCommits()
    {
        _holdCleared = true;
        try
        {
            return ReleaseParkedCommits(MonotonicClock.Nanos, refreshCycleCompleted: false);
        }
        finally
        {
            _holdCleared = false;
        }
    }

    private void OnCommit()
    {
        if (Pending.BufferRelease is not null &&
            ((Pending.Committed & SurfaceStateFields.Buffer) == 0 || Pending.Buffer is null))
        {
            Resource.PostError(
                (uint)WlSurface.Error.NoBuffer,
                "wl_surface.get_release needs a non-null buffer in the same content update");
            return;
        }

        CommitRequested?.Invoke();

        var armsBarrier = _pendingArmBarrier;
        var waitsBarrier = _pendingWaitBarrier;
        var targetTimeNanos = _pendingCommitTimeNanos;
        var held = _pendingHold;
        (_pendingArmBarrier, _pendingWaitBarrier, _pendingCommitTimeNanos, _pendingHold) = (false, false, 0, false);

        var synchronized = SubsurfaceRole is { IsEffectivelySynchronized: true };
        var reason = synchronized ? CommitParkReason.SubsurfaceSync : CommitParkReason.None;

        if (held && !synchronized)
        {
            reason |= CommitParkReason.Held;
        }

        if (waitsBarrier && _barrierArmed && !synchronized)
        {
            reason |= CommitParkReason.FifoBarrier;
        }

        if (targetTimeNanos > 0 && MonotonicClock.Nanos < targetTimeNanos)
        {
            reason |= CommitParkReason.CommitTiming;
        }

        if ((reason & DeferredReasons) != 0 || HasParkedCommits)
        {
            _queue ??= new SurfaceCommitQueue();
            _queue.Park(Pending, reason, targetTimeNanos, armsBarrier);
            return;
        }

        if (synchronized)
        {
            _cached ??= new SurfaceState();
            SurfaceCommit.Move(Pending, _cached);
            return;
        }

        _barrierArmed |= armsBarrier;
        ApplyCachedAndPending();
    }

    public bool ReleaseParkedCommits(long nowNanos, bool refreshCycleCompleted)
    {
        _thread.Assert();

        if (_destroyed)
        {
            return false;
        }

        if (refreshCycleCompleted)
        {
            _barrierArmed = false;
        }

        if (_queue is not { IsEmpty: false } queue)
        {
            return false;
        }

        var released = false;

        while (queue.TryReleaseReady(nowNanos, !_barrierArmed, _holdCleared, out var state, out var armsBarrier))
        {
            released = true;
            ApplyParked(state);
            queue.Recycle(state);
            _barrierArmed |= armsBarrier;
        }

        return released;
    }

    private void ApplyParked(SurfaceState state)
    {
        if (SubsurfaceRole is { IsEffectivelySynchronized: true })
        {
            _cached ??= new SurfaceState();
            SurfaceCommit.Move(state, _cached);
            return;
        }

        ApplyCachedState();
        LastCommitFields = state.Committed;
        SurfaceCommit.Move(state, Current, targetIsCurrent: true);
        FinishCommit();
    }

    public SurfaceStateFields LastCommitFields { get; private set; }

    private void ApplyCachedAndPending()
    {
        ApplyCachedState();
        LastCommitFields = Pending.Committed;
        SurfaceCommit.Move(Pending, Current, targetIsCurrent: true);
        FinishCommit();
    }

    internal void ApplyCachedState()
    {
        if (_cached is { } cached)
        {
            _cached = null;
            LastCommitFields = cached.Committed;
            SurfaceCommit.Move(cached, Current, targetIsCurrent: true);
            cached.Dispose();
            FinishCommit();
        }
    }

    private List<Subsurface>? _commitScratch;

    internal Basin.Protocol.WpViewportResource? ViewportResource { get; set; }

    private void ValidateViewportSource()
    {
        if (ViewportResource is not { IsDestroyed: false } viewport ||
            Current.Buffer is not { } buffer ||
            Current.ViewportSourceWidth < 0 || Current.ViewportSourceHeight < 0)
        {
            return;
        }

        var width = buffer.Width / (double)Current.Scale;
        var height = buffer.Height / (double)Current.Scale;
        if (((int)Current.Transform & 1) != 0)
        {
            (width, height) = (height, width);
        }

        if (Current.ViewportSourceX + Current.ViewportSourceWidth > width ||
            Current.ViewportSourceY + Current.ViewportSourceHeight > height)
        {
            viewport.PostError(
                (uint)Basin.Protocol.WpViewport.Error.OutOfBuffer,
                "viewport source rectangle extends outside the buffer");
        }
    }

    private void FinishCommit()
    {
        Current.UpdateDerivedSize();
        ValidateViewportSource();
        SyncGuardedShadows();

        if (SubsurfacesBelow.Count == 0 && SubsurfacesAbove.Count == 0)
        {
            Committed?.Invoke();
            return;
        }

        var scratch = _commitScratch ??= [];
        scratch.Clear();
        scratch.AddRange(SubsurfacesBelow);
        scratch.AddRange(SubsurfacesAbove);

        foreach (var child in scratch)
        {
            child.ApplyPendingPlacement();
        }

        Committed?.Invoke();

        foreach (var child in scratch)
        {
            child.OnParentCommitted();
        }

        scratch.Clear();
    }

    private List<ManagedShmBuffer>? _guardedShadows;

    private void SyncGuardedShadows()
    {
        if (_guardedShadows is { Count: > 0 })
        {
            var identityDamage = Current.Scale == 1
                && Current.Transform == OutputTransform.Normal
                && Current.ViewportSourceWidth < 0
                && Current.ViewportDestinationWidth < 0;
            foreach (var tracked in _guardedShadows)
            {
                if (!identityDamage)
                {
                    tracked.MarkAllDirty();
                    continue;
                }

                tracked.AccumulateDirty(Current.BufferDamage);
                tracked.AccumulateDirty(Current.SurfaceDamage);
            }
        }

        if (Current.Buffer is not ManagedShmBuffer { IsGuarded: true } guarded)
        {
            return;
        }

        _guardedShadows ??= [];
        if (!_guardedShadows.Contains(guarded))
        {
            _guardedShadows.Add(guarded);
            guarded.Destroyed += () => _guardedShadows.Remove(guarded);
            guarded.MarkAllDirty();
        }

        guarded.SyncShadow();
    }

    internal void ApplyCacheOnDesync() => ApplyCachedState();

    private int _acquireFenceFd = -1;

    public int AcquireFenceFd => _acquireFenceFd;

    public void SetAcquireFence(int syncFileFd)
    {
        if (_acquireFenceFd == syncFileFd)
        {
            return;
        }

        if (_acquireFenceFd >= 0)
        {
            _ = close(_acquireFenceFd);
        }

        _acquireFenceFd = syncFileFd;
    }

    [DllImport("libc")]
    private static extern int close(int fd);

    private void OnDestroyed()
    {
        if (_destroyed)
        {
            return;
        }

        _destroyed = true;
        SetAcquireFence(-1);
        SubsurfaceRole?.OnSurfaceDestroyed();
        _cached?.Dispose();
        _cached = null;
        _queue?.Dispose();
        _queue = null;
        Pending.Dispose();
        Current.Dispose();
        Destroyed?.Invoke();
        BasinCounters.Untrack();
    }
}
