using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

/// <summary>
/// A client's <c>wl_surface</c>. It collects requests into <see cref="Pending"/> and moves them onto <see cref="Current"/> on commit, unless a <see cref="CommitParkReason"/> holds the commit.
/// </summary>
public sealed class Surface
{
    private const CommitParkReason DeferredReasons =
        CommitParkReason.FifoBarrier | CommitParkReason.CommitTiming | CommitParkReason.Held | CommitParkReason.Fence;

    private readonly CompositorGlobal _owner;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private SurfaceState? _cached;
    private SurfaceCommitQueue? _queue;
    private bool _pendingArmBarrier;
    private bool _pendingWaitBarrier;
    private long _pendingCommitTimeNanos;
    private bool _barrierArmed;
    private bool _pendingHold;
    private bool _pendingExplicit;
    private List<ISurfaceCommitFence>? _fences;
    private ISurfaceCommitFence? _pendingFence;
    private Action<ISurfaceCommitFence>? _onFenceSignaled;
    private bool _holdCleared;
    private bool _destroyed;
    private double _clientScale = 1.0;
    private double _serverScaleOverride;
    private Pixman.PixmanRegion32? _scaleScratch;

    internal Surface(CompositorGlobal owner, WlSurfaceResource resource)
        : this(owner, resource, 1.0)
    {
    }

    internal Surface(CompositorGlobal owner, WlSurfaceResource resource, double clientScale)
    {
        _owner = owner;
        Resource = resource;
        _clientScale = clientScale;
        Current.ServerScale = clientScale;
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

    /// <summary>Gets the surface resource.</summary>
    public WlSurfaceResource Resource { get; }

    /// <summary>Gets the state of the last applied commit. Sizes, offsets and surface damage are in logical coordinates.</summary>
    public SurfaceState Current { get; } = new();

    /// <summary>Gets the state that the client is building for its next commit. Extensions add their payloads here before the commit.</summary>
    public SurfaceState Pending { get; } = new();

    /// <summary>Gets the role name, or <see langword="null"/> before a role is set. A surface keeps its role name for life.</summary>
    public string? Role { get; private set; }

    /// <summary>Gets the object that implements the role, or <see langword="null"/> when there is none or the role object was destroyed.</summary>
    public object? RoleObject { get; private set; }

    /// <summary>Gets the subsurface role, or <see langword="null"/> when the surface is not a live subsurface.</summary>
    public Subsurface? SubsurfaceRole { get; internal set; }

    /// <summary>Gets the child subsurfaces stacked below this surface, from bottom to top.</summary>
    public List<Subsurface> SubsurfacesBelow { get; } = [];

    /// <summary>Gets the child subsurfaces stacked above this surface, from bottom to top.</summary>
    public List<Subsurface> SubsurfacesAbove { get; } = [];

    /// <summary>Gets a value indicating whether the current state has a buffer and the surface is not destroyed.</summary>
    public bool IsMapped => Current.Buffer is not null && !_destroyed;

    /// <summary>Gets a value indicating whether the resource is destroyed.</summary>
    public bool IsDestroyed => _destroyed;

    /// <summary>Occurs after a commit applies to <see cref="Current"/>, after the subsurface placement changes and before synchronized children apply their caches.</summary>
    public event Action? Committed;

    /// <summary>Occurs when <c>wl_surface.commit</c> arrives, before any state moves. Extensions latch their pending state here.</summary>
    public event Action? CommitRequested;

    /// <summary>Occurs once, after the surface released its state at resource destruction.</summary>
    public event Action? Destroyed;

    /// <summary>Gets the factor by which the client's surface coordinates exceed logical coordinates. It is 1 for an ordinary client.</summary>
    public double ServerScale => Current.ServerScale;

    /// <summary>Gets a value indicating whether <see cref="SetServerScale"/> overrides the client's default scale.</summary>
    public bool HasServerScaleOverride => _serverScaleOverride > 0;

    /// <summary>Occurs after the server scale changes and the current size is recomputed.</summary>
    public event Action? ServerScaleChanged;

    /// <summary>
    /// Overrides the server scale of this surface. State that the client committed earlier stays in the old units until the next commit.
    /// </summary>
    /// <param name="scale">The scale. It must be positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not positive and finite.</exception>
    public void SetServerScale(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "a server scale must be positive and finite");
        }

        _serverScaleOverride = scale;
        ApplyServerScale(scale);
    }

    /// <summary>Removes the override and returns the surface to its client's default scale.</summary>
    public void ClearServerScale()
    {
        _serverScaleOverride = 0;
        ApplyServerScale(_clientScale);
    }

    internal void SetClientScale(double scale)
    {
        _clientScale = scale;
        if (_serverScaleOverride <= 0)
        {
            ApplyServerScale(scale);
        }
    }

    private void ApplyServerScale(double scale)
    {
        if (_destroyed || Current.ServerScale == scale)
        {
            return;
        }

        Current.ServerScale = scale;
        Current.UpdateDerivedSize();
        ServerScaleChanged?.Invoke();
    }

    private void ScalePendingToLogical()
    {
        var scale = Current.ServerScale;
        var pending = Pending;
        if ((pending.Committed & SurfaceStateFields.SurfaceDamage) != 0)
        {
            ServerScaling.ToLogical(pending.SurfaceDamage, scale, outward: true, _scaleScratch ??= new());
            ServerScaling.ToLogicalOutward(ref pending.SurfaceDamageRects, scale);
        }

        if ((pending.Committed & SurfaceStateFields.OpaqueRegion) != 0 && pending.HasOpaque)
        {
            ServerScaling.ToLogical(pending.Opaque, scale, outward: false, _scaleScratch ??= new());
        }

        if ((pending.Committed & SurfaceStateFields.InputRegion) != 0 && pending.HasInput && !pending.InputIsInfinite)
        {
            ServerScaling.ToLogical(pending.Input, scale, outward: true, _scaleScratch ??= new());
        }

        if ((pending.Committed & SurfaceStateFields.Offset) != 0)
        {
            pending.OffsetX = ServerScaling.ToLogical(pending.OffsetX, scale);
            pending.OffsetY = ServerScaling.ToLogical(pending.OffsetY, scale);
        }
    }

    /// <summary>Tests whether <see cref="TrySetRole"/> succeeds for a role, without setting it.</summary>
    /// <param name="role">The role name.</param>
    /// <returns><see langword="true"/> when the surface has no role, or has the same role and no live role object.</returns>
    public bool CanSetRole(string role) => Role is null || (Role == role && RoleObject is null);

    /// <summary>Gives the surface a role. A surface can take a second role object only for the same role name, after the first one is gone.</summary>
    /// <param name="role">The role name.</param>
    /// <param name="roleObject">The object that implements the role.</param>
    /// <returns><see langword="true"/> when the role is set, or <see langword="false"/> when the surface has a different role or a live role object.</returns>
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

    /// <summary>Drops the role object and the subsurface role. The role name stays.</summary>
    public void ClearRoleObject()
    {
        RoleObject = null;
        SubsurfaceRole = null;
    }

    /// <summary>Gets or sets a value indicating whether a frame limiter holds frame callbacks back. While it is <see langword="true"/>, <see cref="SendFrameDone"/> sends nothing.</summary>
    public bool PacingHeld { get; set; }

    /// <summary>Gets the number of times that the surface sent frame callbacks.</summary>
    public long PacingUses { get; private set; }

    /// <summary>Gets or sets the frame limiter's cycle in nanoseconds, or zero for none. Presentation feedback reports it as the refresh when it is longer.</summary>
    public uint PacingIntervalNanos { get; set; }

    /// <summary>Gets the monotonic time in nanoseconds of the last presentation, or zero before the first.</summary>
    public long PresentAnchorNanos { get; private set; }

    /// <summary>Gets the refresh period in nanoseconds of the output at the last presentation, or zero for no fixed grid.</summary>
    public long PresentAnchorGrid { get; private set; }

    /// <summary>Records a presentation as the anchor for commit-timing targets.</summary>
    /// <param name="timeNanos">The presentation time in monotonic nanoseconds.</param>
    /// <param name="gridNanos">The output's refresh period in nanoseconds, or zero for no fixed grid.</param>
    public void NotePresented(long timeNanos, long gridNanos)
    {
        PresentAnchorNanos = timeNanos;
        PresentAnchorGrid = gridNanos;
    }

    /// <summary>Gets the monotonic time in nanoseconds when the surface last sent frame callbacks.</summary>
    public long LastPacingUseNanos { get; private set; }

    /// <summary>Increments <see cref="PacingUses"/> and stamps <see cref="LastPacingUseNanos"/> with the current time.</summary>
    public void NotePacingUse()
    {
        PacingUses++;
        LastPacingUseNanos = MonotonicClock.Nanos;
    }

    /// <summary>
    /// Sends <c>done</c> to each frame callback of the current state and clears them. It does nothing while <see cref="PacingHeld"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="timestampMs">The timestamp in milliseconds.</param>
    public void SendFrameDone(uint timestampMs)
    {
        if (PacingHeld)
        {
            return;
        }

        if (Current.FrameResources.Count + Current.FrameCallbacks.Count > 0)
        {
            _owner.NotifyFramesDone(this, timestampMs, Current.FrameResources.Count + Current.FrameCallbacks.Count);
        }

        if (Current.FrameResources.Count > 0)
        {
            NotePacingUse();
        }

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

        NotePacingUse();
        foreach (var callback in Current.FrameCallbacks)
        {
            callback.Done(timestampMs);
        }

        Current.FrameCallbacks.Clear();
    }

    private HashSet<OutputGlobal>? _enteredOutputs;

    /// <summary>
    /// Records whether the surface is on an output, and sends <c>enter</c> or <c>leave</c> to each of the client's bindings of it on a change.
    /// </summary>
    /// <param name="output">The output global.</param>
    /// <param name="inside"><see langword="true"/> when the surface overlaps the output.</param>
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

    /// <summary>Gets the outputs that the surface was last told it entered.</summary>
    public IReadOnlyCollection<OutputGlobal> EnteredOutputs =>
        _enteredOutputs ?? (IReadOnlyCollection<OutputGlobal>)Array.Empty<OutputGlobal>();

    /// <summary>Occurs after <see cref="SetOutputPresence"/> changes the entered outputs.</summary>
    public event Action? OutputPresenceChanged;

    /// <summary>Gets the largest scale of the entered outputs, or zero when the surface is on no output.</summary>
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

    /// <summary>
    /// Sends <c>wl_surface.preferred_buffer_scale</c> when the value changes. A client below version 6 gets nothing.
    /// </summary>
    /// <param name="scale">The preferred integer buffer scale.</param>
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

    /// <summary>Gets a value indicating whether commits wait in the park queue.</summary>
    public bool HasParkedCommits => _queue is { IsEmpty: false };

    /// <summary>Gets the earliest target time in monotonic nanoseconds of a commit parked by commit timing, or zero when there is none.</summary>
    public long NextParkedCommitTimeNanos => _queue?.EarliestTargetTimeNanos ?? 0;

    /// <summary>Makes the next commit arm the fifo barrier when it applies.</summary>
    public void SetFifoBarrier() => _pendingArmBarrier = true;

    /// <summary>Makes the next commit wait while the fifo barrier is armed.</summary>
    public void WaitFifoBarrier() => _pendingWaitBarrier = true;

    /// <summary>Gets a value indicating whether the fifo barrier is armed.</summary>
    public bool FifoBarrierArmed => _barrierArmed;

    /// <summary>Disarms the fifo barrier. It does not release parked commits.</summary>
    public void ClearFifoBarrier() => _barrierArmed = false;

    /// <summary>Gets a value indicating whether the next commit carries a target presentation time.</summary>
    public bool HasPendingCommitTime => _pendingCommitTimeNanos > 0;

    /// <summary>Sets the target presentation time of the next commit. The commit parks until the time arrives.</summary>
    /// <param name="targetNanos">The target time in monotonic nanoseconds.</param>
    public void SetCommitTime(long targetNanos) => _pendingCommitTimeNanos = targetNanos;

    /// <summary>
    /// Parks the next commit until <see cref="ReleaseHeldCommits"/>. A synchronized subsurface ignores the hold, because its parent's commit applies it.
    /// </summary>
    public void HoldNextCommit() => _pendingHold = true;

    /// <summary>Tells the surface that the next commit has an explicit acquire point, so it does not wait for an implicit dmabuf fence.</summary>
    public void MarkExplicitAcquire() => _pendingExplicit = true;

    /// <summary>Gets the number of fences that parked commits still wait for.</summary>
    public int ParkedForFences => _fences?.Count ?? 0;

    /// <summary>Gets the output that drew the surface most recently, or <see langword="null"/> when none did or that output is gone.</summary>
    public IOutput? MainOutput => PresentedMain is { Retired: false } main ? main.Output : null;

    internal PresentedSet? PresentedMain { get; set; }

    internal long PresentedMainStamp { get; set; }

    internal PresentedSet? PresentedStampSet { get; set; }

    internal long PresentedStamp { get; set; }

    /// <summary>Makes the next commit park until a fence signals. The surface takes ownership of the fence.</summary>
    /// <param name="fence">The fence.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fence"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The next commit already has a fence.</exception>
    public void ParkFence(ISurfaceCommitFence fence)
    {
        ArgumentNullException.ThrowIfNull(fence);
        if (_pendingFence is not null)
        {
            throw new InvalidOperationException("a commit parks on one fence; this commit already has one");
        }

        _pendingFence = fence;
    }

    private void OnFenceSignaled(ISurfaceCommitFence fence)
    {
        fence.SignaledChanged -= _onFenceSignaled;
        fence.Dispose();
        _fences?.Remove(fence);
        if (_destroyed || _queue is not { } queue || !queue.ClearFence(fence))
        {
            return;
        }

        ReleaseParkedCommits(MonotonicClock.Nanos, refreshCycleCompleted: false);
    }

    /// <summary>
    /// Clears the hold on parked commits and applies each commit at the front of the queue that is now ready.
    /// </summary>
    /// <returns><see langword="true"/> when at least one commit applied.</returns>
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

        if (Current.ServerScale != 1.0)
        {
            ScalePendingToLogical();
        }

        CommitRequested?.Invoke();

        var armsBarrier = _pendingArmBarrier;
        var waitsBarrier = _pendingWaitBarrier;
        var targetTimeNanos = _pendingCommitTimeNanos;
        var held = _pendingHold;
        var explicitAcquire = _pendingExplicit;
        (_pendingArmBarrier, _pendingWaitBarrier, _pendingCommitTimeNanos, _pendingHold, _pendingExplicit) = (false, false, 0, false, false);

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

        var fence = _pendingFence;
        _pendingFence = null;
        if (fence is null && !explicitAcquire && _owner.Loop is { } loop
            && (Pending.Committed & SurfaceStateFields.Buffer) != 0 && Pending.Buffer is { } committed)
        {
            fence = ImplicitFence.For(loop, committed);
        }

        if (fence is { Signaled: true })
        {
            fence.Dispose();
            fence = null;
        }

        if (fence is not null)
        {
            reason |= CommitParkReason.Fence;
            (_fences ??= []).Add(fence);
            fence.SignaledChanged += _onFenceSignaled ??= OnFenceSignaled;
        }

        if ((reason & DeferredReasons) != 0 || HasParkedCommits)
        {
            _queue ??= new SurfaceCommitQueue();
            _queue.Park(Pending, reason, targetTimeNanos, armsBarrier, fence);
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

    /// <summary>
    /// Applies the parked commits at the front of the queue that are ready, in order. It stops at the first commit that is not ready.
    /// </summary>
    /// <param name="nowNanos">The current or predicted time in monotonic nanoseconds.</param>
    /// <param name="refreshCycleCompleted"><see langword="true"/> to disarm the fifo barrier first.</param>
    /// <returns><see langword="true"/> when at least one commit applied.</returns>
    public bool ReleaseParkedCommits(long nowNanos, bool refreshCycleCompleted) =>
        ReleaseParkedCommits(nowNanos, refreshCycleCompleted, gridNanos: -1);

    /// <summary>
    /// Applies the parked commits at the front of the queue that are ready, in order. Held commits stay parked.
    /// </summary>
    /// <param name="nowNanos">The predicted vblank time in monotonic nanoseconds.</param>
    /// <param name="refreshCycleCompleted"><see langword="true"/> to disarm the fifo barrier first.</param>
    /// <param name="gridNanos">The output's refresh period in nanoseconds for target rounding, or a negative value to compare times directly.</param>
    /// <returns><see langword="true"/> when at least one commit applied.</returns>
    public bool ReleaseParkedCommits(long nowNanos, bool refreshCycleCompleted, long gridNanos)
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

        while (queue.TryReleaseReady(nowNanos, !_barrierArmed, _holdCleared, PresentAnchorNanos, PresentAnchorGrid, gridNanos, out var state, out var armsBarrier))
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
        SurfaceCommit.Move(state, Current, targetIsCurrent: true);
        FinishCommit();
    }

    private void ApplyCachedAndPending()
    {
        ApplyCachedState();
        SurfaceCommit.Move(Pending, Current, targetIsCurrent: true);
        FinishCommit();
    }

    internal void ApplyCachedState()
    {
        if (_cached is { } cached)
        {
            _cached = null;
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
            Current.ViewportSourceWidth < 0 || Current.ViewportSourceHeight < 0)
        {
            return;
        }

        if (Current.ViewportDestinationWidth < 0 &&
            (Current.ViewportSourceWidth != Math.Floor(Current.ViewportSourceWidth) ||
             Current.ViewportSourceHeight != Math.Floor(Current.ViewportSourceHeight)))
        {
            viewport.PostError(
                (uint)Basin.Protocol.WpViewport.Error.BadSize,
                "viewport source size is not an integer and no destination size is set");
            return;
        }

        if (Current.Buffer is not { } buffer)
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
                && Current.ServerScale == 1.0
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
            TrackGuardedShadow(_guardedShadows, guarded);
        }

        guarded.SyncShadow();
    }

    private static void TrackGuardedShadow(List<ManagedShmBuffer> shadows, ManagedShmBuffer guarded)
    {
        shadows.Add(guarded);
        guarded.Destroyed += () => shadows.Remove(guarded);
        guarded.MarkAllDirty();
    }

    /// <summary>
    /// Computes the bounding box of the current surface and buffer damage in surface-local logical coordinates, clipped to the surface size.
    /// </summary>
    /// <returns>The damage extents, an empty box when there is no damage, or the whole surface when a transform or viewport is set.</returns>
    public Box CommittedDamageExtents()
    {
        var state = Current;
        var bounds = new Box(0, 0, state.Width, state.Height);
        var surface = state.SurfaceDamage.Extents;
        var buffer = state.BufferDamage.Extents;
        var hasSurface = surface.X2 > surface.X1 && surface.Y2 > surface.Y1;
        var hasBuffer = buffer.X2 > buffer.X1 && buffer.Y2 > buffer.Y1;
        if (!hasSurface && !hasBuffer)
        {
            return default;
        }

        if (state.Transform != OutputTransform.Normal || state.ViewportSourceWidth >= 0 || state.ViewportDestinationWidth >= 0)
        {
            return bounds;
        }

        var scale = Math.Max(1, state.Scale) * state.ServerScale;
        var x1 = int.MaxValue;
        var y1 = int.MaxValue;
        var x2 = int.MinValue;
        var y2 = int.MinValue;
        if (hasSurface)
        {
            x1 = surface.X1;
            y1 = surface.Y1;
            x2 = surface.X2;
            y2 = surface.Y2;
        }

        if (hasBuffer)
        {
            x1 = Math.Min(x1, (int)Math.Floor(buffer.X1 / scale));
            y1 = Math.Min(y1, (int)Math.Floor(buffer.Y1 / scale));
            x2 = Math.Max(x2, (int)Math.Ceiling(buffer.X2 / scale));
            y2 = Math.Max(y2, (int)Math.Ceiling(buffer.Y2 / scale));
        }

        return new Box(x1, y1, x2 - x1, y2 - y1).Intersect(bounds);
    }

    internal void ApplyCacheOnDesync() => ApplyCachedState();

    private int _acquireFenceFd = -1;

    /// <summary>Gets the sync_file that signals when the current content is written, or -1 when there is none. The surface owns it.</summary>
    public int AcquireFenceFd => _acquireFenceFd;

    /// <summary>Replaces the acquire fence and closes the old one. The surface takes ownership of the new fd.</summary>
    /// <param name="syncFileFd">The sync_file fd, or -1 for none.</param>
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
        if (_fences is { } fences)
        {
            foreach (var pendingFence in fences)
            {
                pendingFence.SignaledChanged -= _onFenceSignaled;
                pendingFence.Dispose();
            }

            fences.Clear();
        }

        _pendingFence?.Dispose();
        _pendingFence = null;

        _queue?.Dispose();
        _queue = null;
        _scaleScratch?.Dispose();
        _scaleScratch = null;
        Pending.Dispose();
        Current.Dispose();
        Destroyed?.Invoke();
        BasinCounters.Untrack();
    }
}
