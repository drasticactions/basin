using Basin.Capabilities;

namespace Basin.SoftKeyboard;

/// <summary>
/// The <see cref="ITextInputMethod"/> that the soft keyboard installs as the input-method fallback. It tracks the focused field and sends the keyboard's text to it.
/// The relay mirrors every request to it, so it knows the field even while an input-method client composes.
/// </summary>
public sealed class SoftKeyboardTextInput : ITextInputMethod
{
    private uint _pendingHint;
    private uint _pendingPurpose;
    private string? _pendingText;
    private int _pendingCursor;

    /// <summary>
    /// Gets the surface whose text input is enabled, or <see langword="null"/> for none.
    /// </summary>
    public Surface? ActiveSurface { get; private set; }

    /// <summary>
    /// Gets the field state of the last commit, or <see cref="FieldState.Inactive"/> with no active field.
    /// </summary>
    public FieldState Field { get; private set; }

    /// <summary>
    /// Gets the surrounding text of the last commit, or <see langword="null"/> when the client sent none.
    /// </summary>
    public string? Surrounding { get; private set; }

    /// <summary>
    /// Gets the cursor of the last commit, as a byte offset into the UTF-8 form of <see cref="Surrounding"/>.
    /// </summary>
    public int Cursor { get; private set; }

    /// <summary>
    /// Gets the last cursor rectangle that the client sent, in surface-local coordinates, or <see langword="null"/> before the first one.
    /// </summary>
    public Box? CaretRectangle { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether an input-method client is bound. The relay then drops what this fallback sends, so the text-input route is off.
    /// </summary>
    public bool ExternalInputMethod { get; set; }

    /// <summary>
    /// Gets a value indicating whether the text-input route is open: a field is active and no input-method client is bound.
    /// </summary>
    public bool IsActive => ActiveSurface is not null && !ExternalInputMethod;

    /// <summary>
    /// Gets a value indicating whether the input method can serve text input. It is always <see langword="true"/>.
    /// </summary>
    public bool IsAvailable => true;

    /// <summary>
    /// Gets a value indicating whether the input method grabs the keyboard. It is always <see langword="false"/>.
    /// </summary>
    public bool HasKeyboardGrab => false;

    /// <inheritdoc/>
    public event Action<PreeditString>? Preedit;

    /// <inheritdoc/>
    public event Action<string>? CommitString;

    /// <inheritdoc/>
    public event Action<uint, uint>? DeleteSurroundingText;

    /// <inheritdoc/>
    public event Action? Done;

    /// <summary>
    /// Occurs never, because <see cref="IsAvailable"/> does not change.
    /// </summary>
    public event Action? AvailabilityChanged
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Occurs when a text input on a new surface is enabled, with that surface.
    /// </summary>
    public event Action<Surface>? Activated;

    /// <summary>
    /// Occurs when the active text input is disabled, before <see cref="FieldChanged"/> reports the inactive field.
    /// </summary>
    public event Action? Deactivated;

    /// <summary>
    /// Occurs on each commit of the active text input, and after a deactivation, with the new field state.
    /// </summary>
    public event Action<FieldState>? FieldChanged;

    /// <inheritdoc/>
    public void Activate(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (ReferenceEquals(ActiveSurface, surface))
        {
            return;
        }

        ActiveSurface = surface;
        _pendingHint = 0;
        _pendingPurpose = 0;
        _pendingText = null;
        _pendingCursor = 0;
        Activated?.Invoke(surface);
    }

    /// <inheritdoc/>
    public void Deactivate(Surface surface)
    {
        if (!ReferenceEquals(ActiveSurface, surface))
        {
            return;
        }

        ActiveSurface = null;
        Surrounding = null;
        Cursor = 0;
        Field = FieldState.Inactive;
        Deactivated?.Invoke();
        FieldChanged?.Invoke(Field);
    }

    /// <inheritdoc/>
    public void SurroundingText(string text, uint cursor, uint anchor)
    {
        _pendingText = text;
        _pendingCursor = (int)cursor;
    }

    /// <inheritdoc/>
    public void ContentType(uint hint, uint purpose)
    {
        _pendingHint = hint;
        _pendingPurpose = purpose;
    }

    /// <inheritdoc/>
    public void CursorRectangle(in Box rect) => CaretRectangle = rect;

    /// <inheritdoc/>
    public void Commit(uint serial)
    {
        if (ActiveSurface is null)
        {
            return;
        }

        Surrounding = _pendingText;
        Cursor = _pendingCursor;
        var field = new FieldState(
            true,
            FieldState.KindOf(_pendingPurpose),
            FieldState.AutoCapFor(_pendingHint, _pendingPurpose, Surrounding, Cursor),
            _pendingHint,
            _pendingPurpose);
        Field = field;
        FieldChanged?.Invoke(field);
    }

    /// <inheritdoc/>
    public void ForwardKey(uint timeMs, uint keycode, bool pressed)
    {
    }

    /// <inheritdoc/>
    public void ForwardModifiers(uint depressed, uint latched, uint locked, uint group)
    {
    }

    /// <summary>
    /// Commits a text to the active field, then ends the group.
    /// </summary>
    /// <param name="text">The text to insert at the cursor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public void Send(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        CommitString?.Invoke(text);
        Done?.Invoke();
    }

    /// <summary>
    /// Sets the preedit text of the active field, with the cursor at one position, then ends the group.
    /// </summary>
    /// <param name="text">The preedit text, or <see langword="null"/> to clear it.</param>
    /// <param name="cursor">The cursor position in the preedit text.</param>
    public void SendPreedit(string? text, int cursor)
    {
        Preedit?.Invoke(new PreeditString(text ?? string.Empty, cursor, cursor));
        Done?.Invoke();
    }

    /// <summary>
    /// Deletes text around the cursor of the active field, then ends the group.
    /// </summary>
    /// <param name="beforeBytes">The UTF-8 length to delete before the cursor.</param>
    /// <param name="afterBytes">The UTF-8 length to delete after the cursor.</param>
    public void Delete(uint beforeBytes, uint afterBytes)
    {
        DeleteSurroundingText?.Invoke(beforeBytes, afterBytes);
        Done?.Invoke();
    }
}
