namespace Basin.SoftKeyboard;

public sealed class KeyboardState
{
    private long _lastShiftTap = long.MinValue / 2;
    private long _lastUnlock = long.MinValue / 2;
    private readonly ShiftMode[] _modifiers = new ShiftMode[3];
    private readonly long[] _lastModifierTap = [long.MinValue / 2, long.MinValue / 2, long.MinValue / 2];

    public KeyboardState(KeyTiming? timing = null)
    {
        Timing = timing ?? KeyTiming.Default;
    }

    public KeyTiming Timing { get; set; }

    public ShiftMode Shift { get; private set; }

    public bool SymbolLock { get; private set; }

    public bool ShiftHeld { get; private set; }

    public bool SymbolHeld { get; private set; }

    public bool AutoCap { get; private set; }

    public bool NumLockField { get; set; }

    public bool LayoutNumLock { get; set; }

    public bool SymbolActive => SymbolLock ^ SymbolHeld;

    public bool ShiftActive => (Shift == ShiftMode.Once) ^ ShiftHeld;

    public bool CapActive => ShiftHeld ? Shift == ShiftMode.Off : Shift != ShiftMode.Off;

    public bool Capital => CapActive || AutoCap;

    public ShiftMode Control => _modifiers[0];

    public ShiftMode Alt => _modifiers[1];

    public ShiftMode Super => _modifiers[2];

    public KeyModifiers Modifiers =>
        (_modifiers[0] != ShiftMode.Off ? KeyModifiers.Control : KeyModifiers.None) |
        (_modifiers[1] != ShiftMode.Off ? KeyModifiers.Alt : KeyModifiers.None) |
        (_modifiers[2] != ShiftMode.Off ? KeyModifiers.Super : KeyModifiers.None);

    public ShiftMode ModifierMode(KeyFunction function) => Slot(function) is var slot and >= 0 ? _modifiers[slot] : function == KeyFunction.Shift ? Shift : ShiftMode.Off;

    public event Action? Changed;

    public KeyCap? Resolve(KeyCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (cell.Plain is not { } plain)
        {
            return null;
        }

        var alternate = cell.Alternate ?? plain;
        if ((NumLockField || (LayoutNumLock && Shift == ShiftMode.Lock)) && alternate.IsDigit)
        {
            return ShiftActive ? plain : alternate;
        }

        var useAlternate = plain.IsLetter ? SymbolActive : ShiftActive;
        return useAlternate ? alternate : plain;
    }

    public void TapShift(long now)
    {
        var mode = Shift;
        if (_lastUnlock + Timing.ShiftDoubleTapMs > now)
        {
            _lastUnlock = long.MinValue / 2;
            now = long.MinValue / 2;
        }
        else if (_lastShiftTap + Timing.ShiftDoubleTapMs > now)
        {
            mode = ShiftMode.Lock;
        }
        else if (mode == ShiftMode.Lock)
        {
            mode = ShiftMode.Off;
            _lastUnlock = now;
        }
        else
        {
            mode = mode == ShiftMode.Off ? ShiftMode.Once : ShiftMode.Off;
        }

        _lastShiftTap = now;
        Shift = mode;
        AutoCap = false;
        Changed?.Invoke();
    }

    public void TapSymbol()
    {
        if (SymbolLock)
        {
            SymbolLock = false;
        }
        else
        {
            SymbolLock = true;
            Shift = ShiftMode.Off;
        }

        Changed?.Invoke();
    }

    public void TapModifier(KeyFunction function, long now)
    {
        var slot = Slot(function);
        if (slot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(function), function, "a sticky modifier is Control, Alt or Super");
        }

        var mode = _modifiers[slot];
        if (mode == ShiftMode.Lock)
        {
            mode = ShiftMode.Off;
            now = long.MinValue / 2;
        }
        else if (mode == ShiftMode.Once && _lastModifierTap[slot] + Timing.ShiftDoubleTapMs > now)
        {
            mode = ShiftMode.Lock;
        }
        else
        {
            mode = mode == ShiftMode.Off ? ShiftMode.Once : ShiftMode.Off;
        }

        _lastModifierTap[slot] = now;
        _modifiers[slot] = mode;
        Changed?.Invoke();
    }

    public void SetModifier(KeyFunction function, ShiftMode mode)
    {
        var slot = Slot(function);
        if (slot >= 0 && _modifiers[slot] != mode)
        {
            _modifiers[slot] = mode;
            Changed?.Invoke();
        }
    }

    public void Consume()
    {
        var latched = false;
        for (var slot = 0; slot < _modifiers.Length; slot++)
        {
            if (_modifiers[slot] == ShiftMode.Once)
            {
                _modifiers[slot] = ShiftMode.Off;
                latched = true;
            }
        }

        if (latched && Shift != ShiftMode.Once && !AutoCap)
        {
            Changed?.Invoke();
            return;
        }

        if (Shift == ShiftMode.Once || AutoCap)
        {
            if (Shift == ShiftMode.Once)
            {
                Shift = ShiftMode.Off;
            }

            AutoCap = false;
            Changed?.Invoke();
        }
    }

    public void ClearSymbolLock()
    {
        if (SymbolLock)
        {
            SymbolLock = false;
            Changed?.Invoke();
        }
    }

    public void SetShiftHeld(bool held)
    {
        if (ShiftHeld != held)
        {
            ShiftHeld = held;
            Changed?.Invoke();
        }
    }

    public void SetSymbolHeld(bool held)
    {
        if (SymbolHeld != held)
        {
            SymbolHeld = held;
            Changed?.Invoke();
        }
    }

    public void SetAutoCap(bool autoCap)
    {
        if (AutoCap != autoCap)
        {
            AutoCap = autoCap;
            Changed?.Invoke();
        }
    }

    public void SetShift(ShiftMode mode)
    {
        if (Shift != mode)
        {
            Shift = mode;
            Changed?.Invoke();
        }
    }

    public void Reset()
    {
        Shift = ShiftMode.Off;
        SymbolLock = false;
        ShiftHeld = false;
        SymbolHeld = false;
        AutoCap = false;
        Array.Clear(_modifiers);
        Changed?.Invoke();
    }

    private static int Slot(KeyFunction function) => function switch
    {
        KeyFunction.Control => 0,
        KeyFunction.Alt => 1,
        KeyFunction.Super => 2,
        _ => -1,
    };
}
