using Basin.Capabilities;

namespace Basin.SoftKeyboard;

/// <summary>
/// The show policy of a soft keyboard for the text fields in the compositor's own chrome, which <see cref="IUITextFields"/> reports.
/// </summary>
public sealed class ChromeKeyboardPolicy : IDisposable
{
    private readonly IUITextFields _fields;
    private readonly SoftKeyboardPolicy _policy;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromeKeyboardPolicy"/> class. It subscribes to the focus and touch events of <paramref name="fields"/>.
    /// </summary>
    /// <param name="fields">The chrome text fields.</param>
    /// <param name="policy">The rule for a field that gains the focus.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is <see langword="null"/>.</exception>
    public ChromeKeyboardPolicy(IUITextFields fields, SoftKeyboardPolicy policy = SoftKeyboardPolicy.OnTouchFocus)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _fields = fields;
        _policy = policy;
        _fields.FocusChanged += OnFocusChanged;
        _fields.Touched += OnTouched;
    }

    /// <summary>
    /// Gets a value indicating whether the keyboard must show.
    /// </summary>
    public bool Wanted { get; private set; }

    /// <summary>
    /// Gets the kind of the most recent input. It starts as <see cref="LastInput.Pointer"/>.
    /// </summary>
    public LastInput LastInput { get; private set; } = LastInput.Pointer;

    /// <summary>
    /// Gets the state of the focused chrome field, or <see cref="FieldState.Inactive"/> with none. A password field never has auto-cap.
    /// </summary>
    public FieldState Field => _fields.Focused is { } focused
        ? new FieldState(true, FieldState.KindOf(focused.Purpose), (focused.Hint & FieldState.HintAutoCapitalization) != 0 && focused.Purpose != UITextField.PurposePassword, focused.Hint, focused.Purpose)
        : FieldState.Inactive;

    /// <summary>
    /// Occurs when <see cref="Wanted"/> changes, with the new value and a short reason. It does not occur when the value stays the same.
    /// </summary>
    public event Action<bool, string>? WantedChanged;

    /// <summary>
    /// Shows the keyboard by hand.
    /// </summary>
    public void Raise() => Want(true, "manual");

    /// <summary>
    /// Hides the keyboard by hand.
    /// </summary>
    /// <param name="why">The reason that the event carries.</param>
    public void Hide(string why)
    {
        ArgumentNullException.ThrowIfNull(why);
        Want(false, why);
    }

    /// <summary>
    /// Shows the keyboard for a field that the shell put in front of the user, when a field has the focus and the last input was a touch or the policy is <see cref="SoftKeyboardPolicy.OnAnyFocus"/>.
    /// </summary>
    public void Present()
    {
        if (_fields.Focused is not null && (_policy == SoftKeyboardPolicy.OnAnyFocus || LastInput == LastInput.Touch))
        {
            Want(true, "present");
        }
    }

    /// <summary>
    /// Records a pointer input.
    /// </summary>
    public void NotePointer() => LastInput = LastInput.Pointer;

    /// <summary>
    /// Records a touch without a target. It does not change <see cref="Wanted"/>.
    /// </summary>
    public void NoteTouch() => LastInput = LastInput.Touch;

    /// <summary>
    /// Records a hardware key and hides the keyboard.
    /// </summary>
    public void NoteHardwareKey()
    {
        LastInput = LastInput.Keyboard;
        Want(false, "key");
    }

    /// <summary>
    /// Unsubscribes from the chrome text fields. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fields.FocusChanged -= OnFocusChanged;
        _fields.Touched -= OnTouched;
    }

    private void OnFocusChanged()
    {
        if (_fields.Focused is null)
        {
            Want(false, "blur");
            return;
        }

        if (_policy == SoftKeyboardPolicy.OnAnyFocus || LastInput == LastInput.Touch)
        {
            Want(true, "focus");
        }
    }

    private void OnTouched(UITouchTarget target)
    {
        LastInput = LastInput.Touch;
        switch (target)
        {
            case UITouchTarget.Field:
                Want(true, "touch");
                break;
            case UITouchTarget.Elsewhere:
                Want(false, "touch-away");
                break;
        }
    }

    private void Want(bool wanted, string why)
    {
        if (Wanted == wanted)
        {
            return;
        }

        Wanted = wanted;
        WantedChanged?.Invoke(wanted, why);
    }
}
