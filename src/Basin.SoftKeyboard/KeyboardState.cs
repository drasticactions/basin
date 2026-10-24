namespace Basin.SoftKeyboard;

/// <summary>
/// The shift and symbol state machine of the soft keyboard: the Shift mode, the symbol lock, the two momentary holds, auto-cap and the sticky modifiers.
/// Each method takes the time as an argument, so a test drives the clock by hand.
/// </summary>
public sealed class KeyboardState
{
    private long _lastShiftTap = long.MinValue / 2;
    private long _lastUnlock = long.MinValue / 2;
    private readonly ShiftMode[] _modifiers = new ShiftMode[3];
    private readonly long[] _lastModifierTap = [long.MinValue / 2, long.MinValue / 2, long.MinValue / 2];

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyboardState"/> class.
    /// </summary>
    /// <param name="timing">The timing constants, or <see langword="null"/> for <see cref="KeyTiming.Default"/>.</param>
    public KeyboardState(KeyTiming? timing = null)
    {
        Timing = timing ?? KeyTiming.Default;
    }

    /// <summary>
    /// Gets or sets the timing constants. A change applies to the next tap.
    /// </summary>
    public KeyTiming Timing { get; set; }

    /// <summary>
    /// Gets the tap state of Shift.
    /// </summary>
    public ShiftMode Shift { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a Symbol tap locked the symbol page.
    /// </summary>
    public bool SymbolLock { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a Shift touch is held now.
    /// </summary>
    public bool ShiftHeld { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a Symbol touch is held now.
    /// </summary>
    public bool SymbolHeld { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the field asked for a capital. It clears after one key and on any Shift tap.
    /// </summary>
    public bool AutoCap { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field is a number or phone field, where the number row gives digits without Shift.
    /// </summary>
    public bool NumLockField { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the layout gives digits without Shift while Shift is locked, as AZERTY does.
    /// </summary>
    public bool LayoutNumLock { get; set; }

    /// <summary>
    /// Gets a value indicating whether the symbol page shows: the lock XOR the hold.
    /// </summary>
    public bool SymbolActive => SymbolLock ^ SymbolHeld;

    /// <summary>
    /// Gets a value indicating whether a key that is not a letter shows its alternate: <see cref="ShiftMode.Once"/> XOR the hold.
    /// </summary>
    public bool ShiftActive => (Shift == ShiftMode.Once) ^ ShiftHeld;

    /// <summary>
    /// Gets a value indicating whether Shift makes a capital: the hold with Shift off, or Shift on without the hold.
    /// </summary>
    public bool CapActive => ShiftHeld ? Shift == ShiftMode.Off : Shift != ShiftMode.Off;

    /// <summary>
    /// Gets a value indicating whether the next text key types a capital: <see cref="CapActive"/> or <see cref="AutoCap"/>.
    /// </summary>
    public bool Capital => CapActive || AutoCap;

    /// <summary>
    /// Gets the sticky state of Control.
    /// </summary>
    public ShiftMode Control => _modifiers[0];

    /// <summary>
    /// Gets the sticky state of Alt.
    /// </summary>
    public ShiftMode Alt => _modifiers[1];

    /// <summary>
    /// Gets the sticky state of Super.
    /// </summary>
    public ShiftMode Super => _modifiers[2];

    /// <summary>
    /// Gets the sticky modifiers that are latched or locked now. Shift is not included.
    /// </summary>
    public KeyModifiers Modifiers =>
        (_modifiers[0] != ShiftMode.Off ? KeyModifiers.Control : KeyModifiers.None) |
        (_modifiers[1] != ShiftMode.Off ? KeyModifiers.Alt : KeyModifiers.None) |
        (_modifiers[2] != ShiftMode.Off ? KeyModifiers.Super : KeyModifiers.None);

    /// <summary>
    /// Gives the state of a modifier key.
    /// </summary>
    /// <param name="function">Shift, Control, Alt or Super.</param>
    /// <returns>The state of the modifier, or <see cref="ShiftMode.Off"/> for any other function.</returns>
    public ShiftMode ModifierMode(KeyFunction function) => Slot(function) is var slot and >= 0 ? _modifiers[slot] : function == KeyFunction.Shift ? Shift : ShiftMode.Off;

    /// <summary>
    /// Occurs after any change of the state.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Gives the cap that a cell shows now. A letter shows its alternate on the symbol page, and any other key shows its alternate while Shift is active.
    /// A digit alternate shows without Shift under a number lock, and Shift then shows the plain cap.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The cap to show, or <see langword="null"/> for a cell with no plain cap.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cell"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Applies a Shift tap. A tap within <see cref="KeyTiming.ShiftDoubleTapMs"/> of the previous one locks Shift, and a tap on the lock turns Shift off.
    /// A tap within the same time after an unlock is ignored. The tap clears auto-cap.
    /// </summary>
    /// <param name="now">The time of the tap in milliseconds.</param>
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

    /// <summary>
    /// Toggles the symbol lock. Shift turns off when the lock goes on.
    /// </summary>
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

    /// <summary>
    /// Applies a tap of a sticky modifier. A tap latches it, a second tap within <see cref="KeyTiming.ShiftDoubleTapMs"/> locks it, and a tap on the lock clears it.
    /// </summary>
    /// <param name="function">Control, Alt or Super.</param>
    /// <param name="now">The time of the tap in milliseconds.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="function"/> is not Control, Alt or Super.</exception>
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

    /// <summary>
    /// Sets the state of a sticky modifier. Any other function does nothing.
    /// </summary>
    /// <param name="function">Control, Alt or Super.</param>
    /// <param name="mode">The new state.</param>
    public void SetModifier(KeyFunction function, ShiftMode mode)
    {
        var slot = Slot(function);
        if (slot >= 0 && _modifiers[slot] != mode)
        {
            _modifiers[slot] = mode;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Ends the one-key states after a key that produces input: latched modifiers, <see cref="ShiftMode.Once"/> and auto-cap clear. Locks stay.
    /// </summary>
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

    /// <summary>
    /// Clears the symbol lock.
    /// </summary>
    public void ClearSymbolLock()
    {
        if (SymbolLock)
        {
            SymbolLock = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Sets the momentary Shift hold.
    /// </summary>
    /// <param name="held"><see langword="true"/> while a Shift touch is down.</param>
    public void SetShiftHeld(bool held)
    {
        if (ShiftHeld != held)
        {
            ShiftHeld = held;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Sets the momentary Symbol hold.
    /// </summary>
    /// <param name="held"><see langword="true"/> while a Symbol touch is down.</param>
    public void SetSymbolHeld(bool held)
    {
        if (SymbolHeld != held)
        {
            SymbolHeld = held;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Sets auto-cap from the field.
    /// </summary>
    /// <param name="autoCap"><see langword="true"/> when the next key must type a capital.</param>
    public void SetAutoCap(bool autoCap)
    {
        if (AutoCap != autoCap)
        {
            AutoCap = autoCap;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Sets the Shift mode directly, without the tap rules.
    /// </summary>
    /// <param name="mode">The new mode.</param>
    public void SetShift(ShiftMode mode)
    {
        if (Shift != mode)
        {
            Shift = mode;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Clears Shift, the symbol lock, both holds, auto-cap and the sticky modifiers, and raises <see cref="Changed"/>. The number-lock flags stay.
    /// </summary>
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
