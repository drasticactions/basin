using Basin.Backend.Wayland.Protocol;
using Basin.Capabilities;

namespace Basin.Backend.Wayland;

/// <summary>
/// Implements <see cref="ITextInputMethod"/> over the parent's <c>zwp_text_input_v3</c>, so the host's input method composes into guests.
/// Register it before <c>Freeze</c>.
/// </summary>
public sealed class WaylandSeamTextInput : ITextInputMethod, IDisposable
{
    private readonly WaylandBackend _backend;
    private readonly ZwpTextInputV3? _textInput;
    private Surface? _active;
    private bool _parentFocused;
    private bool _enabled;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WaylandSeamTextInput"/> class.
    /// It creates one parent text input object when the parent has a text input manager and a seat. Otherwise it stays unavailable.
    /// </summary>
    /// <param name="backend">The started backend.</param>
    /// <exception cref="ArgumentNullException"><paramref name="backend"/> is <see langword="null"/>.</exception>
    public WaylandSeamTextInput(WaylandBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        if (backend.ParentTextInputManager is not { } manager || backend.ParentSeat is not { } seat)
        {
            return;
        }

        _textInput = manager.GetTextInput(seat);
        _textInput.Enter += (_, e) => OnParentFocus(_backend.FindOutput(e.Surface) is not null);
        _textInput.Leave += (_, _) => OnParentFocus(false);
        _textInput.PreeditString += (_, e) =>
            Preedit?.Invoke(new PreeditString(e.Text ?? string.Empty, e.CursorBegin, e.CursorEnd));
        _textInput.CommitString += (_, e) => CommitString?.Invoke(e.Text ?? string.Empty);
        _textInput.DeleteSurroundingText += (_, e) =>
            DeleteSurroundingText?.Invoke(e.BeforeLength, e.AfterLength);
        _textInput.Done += (_, _) => Done?.Invoke();
        backend.Flush();
    }

    /// <summary>
    /// Gets or sets the callback that maps a caret rectangle in guest surface coordinates to an output and an output-local rectangle in physical pixels.
    /// A <see langword="null"/> callback or result sends no rectangle to the parent.
    /// </summary>
    public Func<Surface, Box, (WaylandOutput Output, Box Rect)?>? LocateCursorRectangle { get; set; }

    /// <summary>
    /// Gets a value indicating whether the parent text input object exists, the parent focuses one of the outputs, and the bridge is not disposed.
    /// </summary>
    public bool IsAvailable => !_disposed && _parentFocused && _textInput is { IsDestroyed: false };

    /// <summary>
    /// Gets a value indicating whether the bridge grabs the keyboard. It is always <see langword="false"/>, because the host's input method grabs the parent keyboard.
    /// </summary>
    public bool HasKeyboardGrab => false;

    /// <inheritdoc/>
    public event Action<PreeditString>? Preedit;

    /// <inheritdoc/>
    public event Action<string>? CommitString;

    /// <inheritdoc/>
    public event Action<uint, uint>? DeleteSurroundingText;

    /// <summary>
    /// Occurs when the parent sends <c>done</c>. The parent's serial is not forwarded.
    /// </summary>
    public event Action? Done;

    /// <summary>
    /// Occurs when the parent text input focus moves onto or off this compositor's outputs, which changes <see cref="IsAvailable"/>.
    /// The bridge is disabled after each change, and a text input manager must call <see cref="Activate"/> again after the parent focus returns.
    /// </summary>
    public event Action? AvailabilityChanged;

    /// <summary>Disables and destroys the parent text input object. A second call does nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_textInput is { IsDestroyed: false } textInput)
        {
            if (_enabled)
            {
                textInput.Disable();
                textInput.Commit();
            }

            textInput.Dispose();
            _backend.Flush();
        }

        _enabled = false;
    }

    /// <summary>
    /// Makes <paramref name="surface"/> the active guest and enables the parent text input when the parent focuses one of the outputs.
    /// </summary>
    /// <param name="surface">The guest surface that gained text input focus.</param>
    public void Activate(Surface surface)
    {
        _active = surface;
        if (Ready() is not { } textInput || _enabled)
        {
            return;
        }

        _enabled = true;
        textInput.Enable();
        Push(textInput);
    }

    /// <inheritdoc/>
    public void Deactivate(Surface surface)
    {
        if (!ReferenceEquals(surface, _active))
        {
            return;
        }

        _active = null;
        if (Ready() is not { } textInput || !_enabled)
        {
            return;
        }

        _enabled = false;
        textInput.Disable();
        Push(textInput);
    }

    /// <summary>Stages the surrounding text on the parent while the bridge is enabled. <see cref="Commit(uint)"/> sends it.</summary>
    /// <param name="text">The text around the caret. <see langword="null"/> is sent as an empty string.</param>
    /// <param name="cursor">The byte offset of the caret in <paramref name="text"/>.</param>
    /// <param name="anchor">The byte offset of the selection anchor in <paramref name="text"/>.</param>
    public void SurroundingText(string text, uint cursor, uint anchor)
    {
        if (Ready() is { } textInput && _enabled)
        {
            textInput.SetSurroundingText(text ?? string.Empty, (int)cursor, (int)anchor);
        }
    }

    /// <summary>Stages the content type on the parent while the bridge is enabled. <see cref="Commit(uint)"/> sends it.</summary>
    /// <param name="hint">The <c>zwp_text_input_v3</c> content hint flags.</param>
    /// <param name="purpose">The <c>zwp_text_input_v3</c> content purpose.</param>
    public void ContentType(uint hint, uint purpose)
    {
        if (Ready() is { } textInput && _enabled)
        {
            textInput.SetContentType((ZwpTextInputV3.ContentHint)hint, (ZwpTextInputV3.ContentPurpose)purpose);
        }
    }

    /// <summary>
    /// Stages the caret rectangle on the parent after <see cref="LocateCursorRectangle"/> maps it. It converts physical pixels to parent surface units.
    /// </summary>
    /// <param name="rect">The caret rectangle in the active guest's surface coordinates.</param>
    public void CursorRectangle(in Box rect)
    {
        if (Ready() is not { } textInput || !_enabled || _active is not { } surface)
        {
            return;
        }

        if (LocateCursorRectangle?.Invoke(surface, rect) is not { } located)
        {
            return;
        }

        var factor = located.Output.SurfaceToPhysical;
        if (factor <= 0)
        {
            factor = 1;
        }

        textInput.SetCursorRectangle(
            (int)Math.Round(located.Rect.X / factor),
            (int)Math.Round(located.Rect.Y / factor),
            (int)Math.Round(located.Rect.Width / factor),
            (int)Math.Round(located.Rect.Height / factor));
    }

    /// <summary>Commits the staged state to the parent and flushes, while the parent focuses one of the outputs.</summary>
    /// <param name="serial">The guest's commit serial. The parent connection does not use it.</param>
    public void Commit(uint serial)
    {
        if (Ready() is { } textInput)
        {
            Push(textInput);
        }
    }

    /// <summary>Does nothing. The host's input method receives keys from the parent keyboard.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="keycode">The Linux input event code of the key.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    public void ForwardKey(uint timeMs, uint keycode, bool pressed)
    {
    }

    /// <summary>Does nothing. The host's input method receives modifiers from the parent keyboard.</summary>
    /// <param name="depressed">The depressed modifier mask.</param>
    /// <param name="latched">The latched modifier mask.</param>
    /// <param name="locked">The locked modifier mask.</param>
    /// <param name="group">The keyboard layout group.</param>
    public void ForwardModifiers(uint depressed, uint latched, uint locked, uint group)
    {
    }

    private ZwpTextInputV3? Ready()
    {
        if (_disposed || !_parentFocused || _textInput is not { IsDestroyed: false } textInput)
        {
            return null;
        }

        return textInput;
    }

    private void Push(ZwpTextInputV3 textInput)
    {
        textInput.Commit();
        _backend.Flush();
    }

    private void OnParentFocus(bool focused)
    {
        if (_parentFocused == focused)
        {
            return;
        }

        _parentFocused = focused;
        _enabled = false;
        AvailabilityChanged?.Invoke();
    }
}
