using Basin.Diagnostics;

namespace Basin.Seat;

/// <summary>
/// Routes each touch contact to one owner: a gesture, the chrome, a client surface, pointer emulation or a capture. It must be used on the thread that created it.
/// </summary>
public sealed class TouchRouter
{
    private const int Capacity = TouchContacts.Capacity;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly SeatTouch _touch;
    private readonly EdgeSwipeSample[] _replay = new EdgeSwipeSample[EdgeSwipeRecognizer.WithheldCapacity];
    private readonly Slot[] _slots = new Slot[Capacity];

    /// <summary>
    /// Initializes a new instance of the <see cref="TouchRouter"/> class and sets itself as the router of the touch device.
    /// </summary>
    /// <param name="touch">The seat touch device that receives client contacts.</param>
    public TouchRouter(SeatTouch touch)
    {
        ArgumentNullException.ThrowIfNull(touch);
        _touch = touch;
        touch.Router = this;
    }

    /// <summary>
    /// Gets or sets the hit tester that finds client surfaces. With none, no contact reaches a client.
    /// </summary>
    public ITouchHitTester? HitTester { get; set; }

    /// <summary>
    /// Gets or sets the compositor chrome, which is offered each touch down before client surfaces.
    /// </summary>
    public ITouchChrome? Chrome { get; set; }

    /// <summary>
    /// Gets or sets the gesture that sees each event before any other target.
    /// </summary>
    public ITouchGestures? Gestures { get; set; }

    /// <summary>
    /// Gets or sets the mouse emulation for surfaces whose client has not bound <c>wl_touch</c>.
    /// </summary>
    public TouchPointerDriver? Pointer { get; set; }

    /// <summary>
    /// Gets or sets the observer that learns where each routed touch down went.
    /// </summary>
    public ITouchInteractionObserver? Interaction { get; set; }

    /// <summary>
    /// Gets or sets the sink that receives a notice for each touch event.
    /// </summary>
    public ITouchActivitySink? Activity { get; set; }

    /// <summary>
    /// Gets the number of contacts that are down. The router holds at most <see cref="TouchContacts.Capacity"/> contacts.
    /// </summary>
    public int LiveContacts
    {
        get
        {
            var count = 0;
            for (var i = 0; i < Capacity; i++)
            {
                if (_slots[i].Live)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Routes a touch down. A down past the contact capacity is dropped.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    public void Down(uint timeMs, int id, double x, double y)
    {
        _thread.Assert();
        var slot = FindSlot(id);
        if (slot < 0)
        {
            slot = FreeSlot();
            if (slot < 0)
            {
                Activity?.OnTouchActivity();
                return;
            }

            _slots[slot].Id = id;
            _slots[slot].Live = true;
        }

        _slots[slot].X = x;
        _slots[slot].Y = y;
        _slots[slot].Owner = ContactOwner.None;
        _slots[slot].Token = null;
        Activity?.OnTouchActivity();

        switch (Gestures?.Down(id, timeMs, x, y) ?? TouchGestureVerdict.Pass)
        {
            case TouchGestureVerdict.Withhold:
                _slots[slot].Owner = ContactOwner.Withheld;
                return;
            case TouchGestureVerdict.Claim:
                ClaimDownstream(slot);
                return;
            case TouchGestureVerdict.Owned:
            case TouchGestureVerdict.Finish:
                _slots[slot].Owner = ContactOwner.Gesture;
                return;
            case TouchGestureVerdict.Decline:
                Replay(slot, id);
                break;
        }

        RouteDown(slot, timeMs, id, x, y);
    }

    /// <summary>
    /// Routes a motion to the owner of the contact. A motion of an unknown contact is dropped.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    public void Motion(uint timeMs, int id, double x, double y)
    {
        _thread.Assert();
        var slot = FindSlot(id);
        if (slot < 0)
        {
            Activity?.OnTouchActivity();
            return;
        }

        _slots[slot].X = x;
        _slots[slot].Y = y;
        Activity?.OnTouchActivity();

        switch (Gestures?.Motion(id, timeMs, x, y) ?? TouchGestureVerdict.Pass)
        {
            case TouchGestureVerdict.Withhold:
                _slots[slot].Owner = ContactOwner.Withheld;
                return;
            case TouchGestureVerdict.Claim:
                ClaimDownstream(slot);
                return;
            case TouchGestureVerdict.Owned:
            case TouchGestureVerdict.Finish:
                return;
            case TouchGestureVerdict.Decline:
                Replay(slot, id);
                break;
        }

        RouteMotion(slot, timeMs, id, x, y);
    }

    /// <summary>
    /// Routes a lift to the owner of the contact and frees the contact.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    public void Up(uint timeMs, int id)
    {
        _thread.Assert();
        Activity?.OnTouchActivity();
        var slot = FindSlot(id);
        if (slot < 0)
        {
            return;
        }

        switch (Gestures?.Up(id, timeMs) ?? TouchGestureVerdict.Pass)
        {
            case TouchGestureVerdict.Withhold:
            case TouchGestureVerdict.Owned:
            case TouchGestureVerdict.Finish:
            case TouchGestureVerdict.Claim:
                _slots[slot].Live = false;
                return;
            case TouchGestureVerdict.Decline:
                Replay(slot, id);
                break;
        }

        RouteUp(slot, timeMs, id);
        _slots[slot].Live = false;
    }

    /// <summary>
    /// Ends a group of touch events. It sends <c>wl_touch.frame</c> to every client that received an event in the group.
    /// </summary>
    public void Frame()
    {
        _thread.Assert();
        _touch.NotifyFrame();
    }

    /// <summary>
    /// Cancels every contact on every target and forgets them, for example when the touch device goes away.
    /// </summary>
    public void Cancel()
    {
        _thread.Assert();
        Activity?.OnTouchActivity();
        Gestures?.Cancel();
        Chrome?.Cancel();
        Pointer?.Cancel();
        _touch.NotifyCancel();
        for (var i = 0; i < Capacity; i++)
        {
            if (_slots[i].Live && _slots[i].Owner == ContactOwner.Captured)
            {
                _slots[i].Capture?.Cancel();
            }

            _slots[i].Live = false;
            _slots[i].Token = null;
            _slots[i].Capture = null;
        }
    }

    /// <summary>
    /// Takes a live contact from its owner and sends its later events to <paramref name="capture"/>. A client that owned it receives <c>wl_touch.cancel</c>.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="capture">The receiver of the later events.</param>
    /// <returns><see langword="false"/> when the contact is not down.</returns>
    public bool Capture(int id, ITouchCapture capture)
    {
        _thread.Assert();
        ArgumentNullException.ThrowIfNull(capture);
        var slot = FindSlot(id);
        if (slot < 0)
        {
            return false;
        }

        if (_slots[slot].Owner == ContactOwner.Client)
        {
            _touch.NotifyCancel();
            for (var i = 0; i < Capacity; i++)
            {
                if (i != slot && _slots[i].Live && _slots[i].Owner == ContactOwner.Client)
                {
                    _slots[i].Owner = ContactOwner.Dead;
                    _slots[i].Token = null;
                }
            }
        }

        _slots[slot].Owner = ContactOwner.Captured;
        _slots[slot].Token = null;
        _slots[slot].Capture = capture;
        return true;
    }

    /// <summary>
    /// Gets the last layout position of a live contact.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns><see langword="false"/> when the contact is not down.</returns>
    public bool TryGetPosition(int id, out double x, out double y)
    {
        _thread.Assert();
        var slot = FindSlot(id);
        if (slot < 0)
        {
            x = 0;
            y = 0;
            return false;
        }

        x = _slots[slot].X;
        y = _slots[slot].Y;
        return true;
    }

    /// <summary>
    /// Feeds a synthetic touch down to the gestures only. A claim cancels every routed contact. Nothing reaches a client.
    /// </summary>
    /// <param name="id">The synthetic contact id. It must be negative.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns>The verdict of the gestures.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is zero or more.</exception>
    public TouchGestureVerdict SyntheticDown(int id, uint timeMs, double x, double y)
    {
        _thread.Assert();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, 0);
        return Synthetic(Gestures?.Down(id, timeMs, x, y) ?? TouchGestureVerdict.Pass);
    }

    /// <summary>
    /// Feeds a synthetic motion to the gestures only. A decline drops the withheld samples instead of a replay.
    /// </summary>
    /// <param name="id">The synthetic contact id. It must be negative.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns>The verdict of the gestures.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is zero or more.</exception>
    public TouchGestureVerdict SyntheticMotion(int id, uint timeMs, double x, double y)
    {
        _thread.Assert();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, 0);
        return Synthetic(Gestures?.Motion(id, timeMs, x, y) ?? TouchGestureVerdict.Pass);
    }

    /// <summary>
    /// Feeds a synthetic lift to the gestures only.
    /// </summary>
    /// <param name="id">The synthetic contact id. It must be negative.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns>The verdict of the gestures.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is zero or more.</exception>
    public TouchGestureVerdict SyntheticUp(int id, uint timeMs)
    {
        _thread.Assert();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, 0);
        return Synthetic(Gestures?.Up(id, timeMs) ?? TouchGestureVerdict.Pass);
    }

    private TouchGestureVerdict Synthetic(TouchGestureVerdict verdict)
    {
        switch (verdict)
        {
            case TouchGestureVerdict.Claim:
                ClaimDownstream(claiming: -1);
                break;
            case TouchGestureVerdict.Decline:
                _ = Gestures?.TakeWithheld(_replay) ?? 0;
                break;
        }

        return verdict;
    }

    private void RouteDown(int slot, uint timeMs, int id, double x, double y)
    {
        var kind = TouchTargetKind.None;
        Surface? target = null;
        if (Chrome?.TryPress(id, timeMs, x, y) == true)
        {
            if (_slots[slot].Owner == ContactOwner.None)
            {
                _slots[slot].Owner = ContactOwner.Chrome;
            }

            kind = TouchTargetKind.Chrome;
        }
        else if (HitTester?.TryHit(x, y, out var hit) == true && hit.Surface is { } surface)
        {
            target = surface;
            if (_touch.Accepts(surface))
            {
                _slots[slot].Owner = ContactOwner.Client;
                _slots[slot].Token = hit.Token;
                _touch.NotifyDown(surface, timeMs, id, hit.LocalX, hit.LocalY);
                kind = TouchTargetKind.Client;
            }
            else if (Pointer?.TryClaim(id, surface, timeMs, x, y) == true)
            {
                _slots[slot].Owner = ContactOwner.Pointer;
                kind = TouchTargetKind.Pointer;
            }
        }
        else if (Pointer?.TryClaim(id, null, timeMs, x, y) == true)
        {
            _slots[slot].Owner = ContactOwner.Pointer;
            kind = TouchTargetKind.Pointer;
        }

        Interaction?.OnTouchInteraction(id, kind, target);
    }

    private void RouteMotion(int slot, uint timeMs, int id, double x, double y)
    {
        switch (_slots[slot].Owner)
        {
            case ContactOwner.Chrome:
                Chrome?.Motion(id, timeMs, x, y);
                break;
            case ContactOwner.Client:
                if (HitTester?.TryMap(_slots[slot].Token, x, y, out var localX, out var localY) == true)
                {
                    _touch.NotifyMotion(timeMs, id, localX, localY);
                }
                else
                {
                    _touch.NotifyUp(timeMs, id);
                    _slots[slot].Owner = ContactOwner.Dead;
                    _slots[slot].Token = null;
                }

                break;
            case ContactOwner.Pointer:
                Pointer?.Motion(id, timeMs, x, y);
                break;
            case ContactOwner.Captured:
                _slots[slot].Capture?.Motion(id, timeMs, x, y);
                break;
        }
    }

    private void RouteUp(int slot, uint timeMs, int id)
    {
        switch (_slots[slot].Owner)
        {
            case ContactOwner.Chrome:
                Chrome?.Release(id, timeMs, _slots[slot].X, _slots[slot].Y);
                break;
            case ContactOwner.Client:
                _touch.NotifyUp(timeMs, id);
                break;
            case ContactOwner.Pointer:
                Pointer?.Release(id, timeMs);
                break;
            case ContactOwner.Captured:
                _slots[slot].Capture?.Up(id, timeMs);
                break;
        }

        _slots[slot].Token = null;
        _slots[slot].Capture = null;
    }

    private void ClaimDownstream(int claiming)
    {
        Chrome?.Cancel();
        Pointer?.Cancel();
        _touch.NotifyCancel();
        for (var i = 0; i < Capacity; i++)
        {
            if (!_slots[i].Live)
            {
                continue;
            }

            if (i != claiming && _slots[i].Owner == ContactOwner.Captured)
            {
                _slots[i].Capture?.Cancel();
            }

            _slots[i].Token = null;
            _slots[i].Capture = null;
            _slots[i].Owner = i == claiming ? ContactOwner.Gesture : ContactOwner.Dead;
        }
    }

    private void Replay(int slot, int id)
    {
        var count = Gestures?.TakeWithheld(_replay) ?? 0;
        for (var i = 0; i < count; i++)
        {
            var sample = _replay[i];
            if (sample.Down)
            {
                RouteDown(slot, sample.TimeMs, id, sample.X, sample.Y);
            }
            else
            {
                RouteMotion(slot, sample.TimeMs, id, sample.X, sample.Y);
            }
        }
    }

    private int FindSlot(int id)
    {
        for (var i = 0; i < Capacity; i++)
        {
            if (_slots[i].Live && _slots[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private int FreeSlot()
    {
        for (var i = 0; i < Capacity; i++)
        {
            if (!_slots[i].Live)
            {
                return i;
            }
        }

        return -1;
    }

    private enum ContactOwner
    {
        None,

        Withheld,

        Gesture,

        Chrome,

        Client,

        Pointer,

        Captured,

        Dead,
    }

    private struct Slot
    {
        public int Id;

        public ContactOwner Owner;

        public object? Token;

        public ITouchCapture? Capture;

        public double X;

        public double Y;

        public bool Live;
    }
}
