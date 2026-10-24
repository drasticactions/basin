using Basin.Config;

namespace Basin.WindowManager;

/// <summary>
/// A manager's key bindings, each owned by a mode. Only the bindings of the live <see cref="Mode"/> are enabled, and none while the session is locked.
/// </summary>
/// <typeparam name="TMode">The manager's mode enumeration.</typeparam>
public sealed class WmBindingTable<TMode>
    where TMode : struct, Enum
{
    private readonly RiverWindowManager _wm;
    private readonly TMode _defaultMode;
    private readonly List<(TMode Mode, KeyBinding Binding)> _bindings = [];
    private readonly HashSet<WmSeat> _armed = [];
    private bool _locked;

    /// <summary>
    /// Initializes a new instance of the <see cref="WmBindingTable{TMode}"/> class.
    /// </summary>
    /// <param name="wm">The manager whose bindings this table creates.</param>
    /// <param name="defaultMode">The mode that a binding gets when it names none, and the first live mode.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wm"/> is <see langword="null"/>.</exception>
    public WmBindingTable(RiverWindowManager wm, TMode defaultMode = default)
    {
        ArgumentNullException.ThrowIfNull(wm);
        _wm = wm;
        _defaultMode = defaultMode;
        Mode = defaultMode;
    }

    /// <summary>
    /// Gets a value indicating whether the compositor serves key bindings.
    /// </summary>
    public bool IsSupported => _wm.Bindings.IsSupported;

    /// <summary>
    /// Gets or sets the live mode. A change takes effect at the next <see cref="Apply"/>.
    /// </summary>
    public TMode Mode { get; set; }

    /// <summary>
    /// Gets a value indicating whether a locked session disables every binding. The default is <see langword="true"/>.
    /// </summary>
    public bool DisableWhileLocked { get; init; } = true;

    /// <summary>
    /// Gets or sets the test for a chord that a user hotkey holds. A default-mode binding from <see cref="Bind(WmSeat, uint, Modifiers, Action, TMode?)"/> is skipped when it returns <see langword="true"/>.
    /// </summary>
    public Func<uint, Modifiers, bool>? Shadowed { get; set; }

    /// <summary>
    /// Runs the manager's binding definitions once for each seat that is new to the table. It does nothing when key bindings are not served.
    /// </summary>
    /// <param name="context">The manage sequence in progress.</param>
    /// <param name="arm">The definitions for one seat.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="arm"/> is <see langword="null"/>.</exception>
    public void Arm(ManageContext context, Action<WmSeat> arm)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arm);
        if (!IsSupported)
        {
            return;
        }

        foreach (var seat in context.Seats)
        {
            if (_armed.Add(seat))
            {
                arm(seat);
            }
        }
    }

    /// <summary>
    /// Binds a keysym name, as <see cref="Bind(WmSeat, uint, Modifiers, Action, TMode?)"/> does.
    /// </summary>
    /// <param name="seat">The seat whose keyboard the binding watches.</param>
    /// <param name="keysym">The keysym name.</param>
    /// <param name="modifiers">The modifiers that must be held.</param>
    /// <param name="pressed">The action to run on a press.</param>
    /// <param name="mode">The owning mode, or <see langword="null"/> for the default mode.</param>
    /// <returns>The binding, or <see langword="null"/> when <see cref="Shadowed"/> skipped it.</returns>
    /// <exception cref="ArgumentException">No keysym has the name.</exception>
    public KeyBinding? Bind(WmSeat seat, string keysym, Modifiers modifiers, Action pressed, TMode? mode = null) =>
        Bind(seat, Keysym.Require(keysym), modifiers, pressed, mode);

    /// <summary>
    /// Binds a keysym that runs an action and then asks for a manage sequence. A binding of the live mode is enabled now, so call it in a manage sequence.
    /// </summary>
    /// <param name="seat">The seat whose keyboard the binding watches.</param>
    /// <param name="keysym">The xkbcommon keysym value.</param>
    /// <param name="modifiers">The modifiers that must be held.</param>
    /// <param name="pressed">The action to run on a press.</param>
    /// <param name="mode">The owning mode, or <see langword="null"/> for the default mode. Only a binding with no mode can be shadowed.</param>
    /// <returns>The binding, or <see langword="null"/> when <see cref="Shadowed"/> skipped it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> or <paramref name="pressed"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The compositor does not serve key bindings.</exception>
    public KeyBinding? Bind(WmSeat seat, uint keysym, Modifiers modifiers, Action pressed, TMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(pressed);
        var owner = mode ?? _defaultMode;
        if (mode is null && Shadowed?.Invoke(keysym, modifiers) == true)
        {
            return null;
        }

        var binding = _wm.Bindings.Bind(seat, keysym, modifiers, () =>
        {
            pressed();
            _wm.RequestManage();
        });
        Add(owner, binding);
        return binding;
    }

    /// <summary>
    /// Binds a keysym in the default mode without the <see cref="Shadowed"/> test, for the user's own hotkeys.
    /// </summary>
    /// <param name="seat">The seat whose keyboard the binding watches.</param>
    /// <param name="keysym">The xkbcommon keysym value.</param>
    /// <param name="modifiers">The modifiers that must be held.</param>
    /// <param name="pressed">The action to run on a press.</param>
    /// <returns>The new binding.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> or <paramref name="pressed"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The compositor does not serve key bindings.</exception>
    public KeyBinding BindUnshadowed(WmSeat seat, uint keysym, Modifiers modifiers, Action pressed)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(pressed);
        var binding = _wm.Bindings.Bind(seat, keysym, modifiers, () =>
        {
            pressed();
            _wm.RequestManage();
        });
        Add(_defaultMode, binding);
        return binding;
    }

    /// <summary>
    /// Binds a keysym name with no modifiers that runs an action on release and then asks for a manage sequence.
    /// </summary>
    /// <param name="seat">The seat whose keyboard the binding watches.</param>
    /// <param name="keysym">The keysym name.</param>
    /// <param name="released">The action to run on a release.</param>
    /// <param name="mode">The owning mode, or <see langword="null"/> for the default mode.</param>
    /// <returns>The new binding.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> or <paramref name="released"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No keysym has the name.</exception>
    /// <exception cref="NotSupportedException">The compositor does not serve key bindings.</exception>
    public KeyBinding BindRelease(WmSeat seat, string keysym, Action released, TMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(released);
        var binding = _wm.Bindings.Bind(seat, keysym, Modifiers.None);
        binding.Released += () =>
        {
            released();
            _wm.RequestManage();
        };
        Add(mode ?? _defaultMode, binding);
        return binding;
    }

    /// <summary>
    /// Enables the bindings of the live mode and disables all others. While locked, it disables all of them unless <see cref="DisableWhileLocked"/> is <see langword="false"/>.
    /// </summary>
    /// <param name="locked">Whether the session is locked.</param>
    /// <exception cref="InvalidOperationException">A binding must change and no manage sequence is in progress.</exception>
    public void Apply(bool locked)
    {
        _locked = locked;
        foreach (var (mode, binding) in _bindings)
        {
            var want = Live(mode);
            if (want && !binding.IsEnabled)
            {
                binding.Enable();
            }
            else if (!want && binding.IsEnabled)
            {
                binding.Disable();
            }
        }
    }

    /// <summary>
    /// Destroys every binding and forgets the armed seats, so the next <see cref="Arm"/> defines all bindings again. The destroyed bindings leave the list of <see cref="WmBindings"/>, so a later submap does not touch them.
    /// </summary>
    public void Reset()
    {
        foreach (var (_, binding) in _bindings)
        {
            binding.Destroy();
        }

        _bindings.Clear();
        _armed.Clear();
    }

    private void Add(TMode mode, KeyBinding binding)
    {
        if (Live(mode))
        {
            binding.Enable();
        }

        _bindings.Add((mode, binding));
    }

    private bool Live(TMode mode) =>
        !(_locked && DisableWhileLocked) && EqualityComparer<TMode>.Default.Equals(mode, Mode);
}
