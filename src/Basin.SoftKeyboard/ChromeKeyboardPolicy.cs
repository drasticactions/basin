using Basin.Capabilities;

namespace Basin.SoftKeyboard;

public sealed class ChromeKeyboardPolicy : IDisposable
{
    private readonly IUITextFields _fields;
    private readonly SoftKeyboardPolicy _policy;
    private bool _disposed;

    public ChromeKeyboardPolicy(IUITextFields fields, SoftKeyboardPolicy policy = SoftKeyboardPolicy.OnTouchFocus)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _fields = fields;
        _policy = policy;
        _fields.FocusChanged += OnFocusChanged;
        _fields.Touched += OnTouched;
    }

    public bool Wanted { get; private set; }

    public LastInput LastInput { get; private set; } = LastInput.Pointer;

    public FieldState Field => _fields.Focused is { } focused
        ? new FieldState(true, FieldState.KindOf(focused.Purpose), (focused.Hint & FieldState.HintAutoCapitalization) != 0 && focused.Purpose != UITextField.PurposePassword, focused.Hint, focused.Purpose)
        : FieldState.Inactive;

    public event Action<bool, string>? WantedChanged;

    public void Raise() => Want(true, "manual");

    public void Hide(string why)
    {
        ArgumentNullException.ThrowIfNull(why);
        Want(false, why);
    }

    public void Present()
    {
        if (_fields.Focused is not null && (_policy == SoftKeyboardPolicy.OnAnyFocus || LastInput == LastInput.Touch))
        {
            Want(true, "present");
        }
    }

    public void NotePointer() => LastInput = LastInput.Pointer;

    public void NoteTouch() => LastInput = LastInput.Touch;

    public void NoteHardwareKey()
    {
        LastInput = LastInput.Keyboard;
        Want(false, "key");
    }

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
