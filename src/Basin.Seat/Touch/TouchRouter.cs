using Basin.Diagnostics;

namespace Basin.Seat;

public sealed class TouchRouter
{
    private const int Capacity = TouchContacts.Capacity;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly SeatTouch _touch;
    private readonly EdgeSwipeSample[] _replay = new EdgeSwipeSample[EdgeSwipeRecognizer.WithheldCapacity];
    private readonly Slot[] _slots = new Slot[Capacity];

    public TouchRouter(SeatTouch touch)
    {
        ArgumentNullException.ThrowIfNull(touch);
        _touch = touch;
        touch.Router = this;
    }

    public ITouchHitTester? HitTester { get; set; }

    public ITouchChrome? Chrome { get; set; }

    public ITouchGestures? Gestures { get; set; }

    public TouchPointerDriver? Pointer { get; set; }

    public ITouchInteractionObserver? Interaction { get; set; }

    public ITouchActivitySink? Activity { get; set; }

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

    public void Frame()
    {
        _thread.Assert();
        _touch.NotifyFrame();
    }

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

    public TouchGestureVerdict SyntheticDown(int id, uint timeMs, double x, double y)
    {
        _thread.Assert();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, 0);
        return Synthetic(Gestures?.Down(id, timeMs, x, y) ?? TouchGestureVerdict.Pass);
    }

    public TouchGestureVerdict SyntheticMotion(int id, uint timeMs, double x, double y)
    {
        _thread.Assert();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, 0);
        return Synthetic(Gestures?.Motion(id, timeMs, x, y) ?? TouchGestureVerdict.Pass);
    }

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
