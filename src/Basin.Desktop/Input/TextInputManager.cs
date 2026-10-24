using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwp_text_input_manager_v3</c> global and connects the enabled text input of the focused client to an <see cref="ITextInputMethod"/>. The consumer moves the focus through <see cref="NotifyFocus"/>.
/// </summary>
public sealed class TextInputManager : IDisposable, ITextInputFocus
{
    /// <summary>
    /// The highest global version that the manager advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _textInputGlobal;
    private readonly Seat.Seat? _seat;
    private readonly ITextInputMethod? _method;
    private readonly List<TextInput> _textInputs = [];
    private Surface? _focus;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextInputManager"/> class and creates the global. With a seat, it forwards modifier changes to the input method and claims the keyboard text commit hook when it is free.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="seat">The seat to follow, or <see langword="null"/>.</param>
    /// <param name="method">The input method that serves text input. When it is <see langword="null"/>, text inputs get only text from <see cref="TryCommitString"/>.</param>
    public TextInputManager(WlServerDisplay display, Seat.Seat? seat, ITextInputMethod? method)
    {
        ArgumentNullException.ThrowIfNull(display);
        _seat = seat;
        _method = method;
        _textInputGlobal = display.CreateGlobal(ZwpTextInputManagerV3.Interface, Version, OnBindTextInput);
        if (_seat is { } withSeat)
        {
            withSeat.Keyboard.ModifiersChanged += OnModifiersChanged;
            withSeat.Keyboard.CommitText ??= TryCommitString;
        }

        if (_method is { } live)
        {
            live.Preedit += OnPreedit;
            live.CommitString += OnCommitString;
            live.DeleteSurroundingText += OnDeleteSurrounding;
            live.Done += OnMethodDone;
            live.AvailabilityChanged += OnActiveStateChanged;
        }
    }

    /// <summary>
    /// Occurs when the focused client enables its text input, or when the input method becomes available while it is enabled.
    /// </summary>
    public event Action<Surface>? TextInputActivated;

    /// <summary>
    /// Occurs when the active text input state ends, because the client disabled it or the input method became unavailable.
    /// </summary>
    public event Action? TextInputDeactivated;

    /// <summary>
    /// Occurs when the input method commits text while no text input is active, so that the consumer can deliver it in a different way.
    /// </summary>
    public event Action<string>? CommitStringUnclaimed;

    /// <summary>
    /// Occurs when the active text input commits a caret rectangle, with the focused surface and the rectangle in surface-local logical pixels.
    /// </summary>
    public event Action<Surface, Box>? CaretMoved;

    /// <summary>
    /// Stops following the seat and the input method, releases the keyboard text commit hook when it holds it, and removes the global.
    /// </summary>
    public void Dispose()
    {
        if (_seat is { } withSeat)
        {
            withSeat.Keyboard.ModifiersChanged -= OnModifiersChanged;
            if (withSeat.Keyboard.CommitText == TryCommitString)
            {
                withSeat.Keyboard.CommitText = null;
            }
        }

        if (_method is { } live)
        {
            live.Preedit -= OnPreedit;
            live.CommitString -= OnCommitString;
            live.DeleteSurroundingText -= OnDeleteSurrounding;
            live.Done -= OnMethodDone;
            live.AvailabilityChanged -= OnActiveStateChanged;
        }

        _textInputGlobal.Dispose();
    }

    /// <summary>
    /// Gets a value indicating whether the input method grabs the keyboard, so that key events go to <see cref="ForwardKey"/>.
    /// </summary>
    public bool HasKeyboardGrab => _method?.HasKeyboardGrab ?? false;

    /// <inheritdoc/>
    public bool HasActiveTextInput => ActiveTextInput() is not null;

    /// <inheritdoc/>
    public bool TryCommitString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (ActiveTextInput() is not { } active)
        {
            return false;
        }

        active.ApplyCommit(text);
        active.ApplyDone();
        return true;
    }

    /// <summary>
    /// Sends a key event to the keyboard grab of the input method. It does nothing without an input method or a grab.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="key">The evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    public void ForwardKey(uint timeMs, uint key, bool pressed)
    {
        _method?.ForwardKey(timeMs, key, pressed);
    }

    private void OnModifiersChanged()
    {
        if (_method is { } method && _seat is { } seat)
        {
            var modifiers = seat.Keyboard.ModifierState;
            method.ForwardModifiers(modifiers.Depressed, modifiers.Latched, modifiers.Locked, modifiers.Group);
        }
    }

    private void OnPreedit(PreeditString preedit) =>
        ActiveTextInput()?.ApplyPreedit(preedit.Text, preedit.CursorBegin, preedit.CursorEnd);

    private void OnCommitString(string text)
    {
        if (ActiveTextInput() is { } active)
        {
            active.ApplyCommit(text);
        }
        else
        {
            CommitStringUnclaimed?.Invoke(text);
        }
    }

    private void OnDeleteSurrounding(uint before, uint after) =>
        ActiveTextInput()?.ApplyDeleteSurrounding(before, after);

    private void OnMethodDone() => ActiveTextInput()?.ApplyDone();

    /// <inheritdoc/>
    public void NotifyFocus(Surface? surface)
    {
        if (_focus == surface)
        {
            return;
        }

        foreach (var textInput in _textInputs)
        {
            if (_focus is not null && textInput.Owns(_focus))
            {
                textInput.SendLeave(_focus);
            }
        }

        _focus = surface;
        foreach (var textInput in _textInputs)
        {
            if (surface is not null && textInput.Owns(surface))
            {
                textInput.SendEnter(surface);
            }
        }
    }

    private TextInput? ActiveTextInput()
    {
        if (_focus is null)
        {
            return null;
        }

        foreach (var textInput in _textInputs)
        {
            if (textInput.Enabled && textInput.Owns(_focus))
            {
                return textInput;
            }
        }

        return null;
    }

    private void OnActiveStateChanged()
    {
        var active = ActiveTextInput();
        if (active is not null)
        {
            _method?.Activate(_focus!);
            SendState(active);
            TextInputActivated?.Invoke(_focus!);
        }
        else
        {
            if (_focus is { } focus)
            {
                _method?.Deactivate(focus);
            }

            TextInputDeactivated?.Invoke();
        }
    }

    private void SendState(TextInput textInput)
    {
        if (_method is not { } method)
        {
            return;
        }

        if (textInput.Surrounding is { } surrounding)
        {
            method.SurroundingText(surrounding.Text, (uint)surrounding.Cursor, (uint)surrounding.Anchor);
        }

        if (textInput.ContentType is { } contentType)
        {
            method.ContentType(contentType.Hint, contentType.Purpose);
        }

        if (textInput.CursorRectangle is { } caret)
        {
            method.CursorRectangle(ServerScaling.ToLogicalNearest(caret, _focus?.ServerScale ?? 1.0));
        }

        method.Commit(0);
    }

    private void OnBindTextInput(WlClient client, uint version, uint id)
    {
        var manager = new ZwpTextInputManagerV3Resource(client, version, id);
        manager.GetTextInput += (_, e) =>
        {
            var resource = new ZwpTextInputV3Resource(client, manager.Version, e.Id);
            var textInput = new TextInput(this, resource);
            _textInputs.Add(textInput);
            resource.Destroyed += (_, _) =>
            {
                var wasActive = textInput == ActiveTextInput();
                _textInputs.Remove(textInput);
                if (wasActive)
                {
                    OnActiveStateChanged();
                }
            };

            if (_focus is not null && textInput.Owns(_focus))
            {
                textInput.SendEnter(_focus);
            }
        };
    }

    private sealed class TextInput
    {
        private readonly TextInputManager _owner;
        private readonly ZwpTextInputV3Resource _resource;
        private uint _serial;
        private bool _pendingEnabled;
        private (string Text, int Cursor, int Anchor)? _pendingSurrounding;
        private (uint Hint, uint Purpose)? _pendingContentType;
        private Box? _pendingCursorRectangle;

        public TextInput(TextInputManager owner, ZwpTextInputV3Resource resource)
        {
            _owner = owner;
            _resource = resource;

            resource.Enable += (_, _) => _pendingEnabled = true;
            resource.Disable += (_, _) => _pendingEnabled = false;
            resource.SetSurroundingText += (_, e) => _pendingSurrounding = (e.Text, e.Cursor, e.Anchor);
            resource.SetContentType += (_, e) => _pendingContentType = ((uint)e.Hint, (uint)e.Purpose);
            resource.SetCursorRectangle += (_, e) =>
                _pendingCursorRectangle = new Box(e.X, e.Y, e.Width, e.Height);
            resource.Commit += (_, _) =>
            {
                _serial++;
                var wasEnabled = Enabled;
                Enabled = _pendingEnabled;
                Surrounding = _pendingSurrounding;
                ContentType = _pendingContentType;
                var caretMoved = _pendingCursorRectangle is { } pending && pending != CursorRectangle;
                CursorRectangle = _pendingCursorRectangle;

                var active = _owner.ActiveTextInput();
                if (active == this && caretMoved && Enabled && _owner._focus is { } focused && CursorRectangle is { } moved)
                {
                    _owner.CaretMoved?.Invoke(focused, ServerScaling.ToLogicalNearest(moved, focused.ServerScale));
                }

                if (active == this)
                {
                    if (!wasEnabled)
                    {
                        _owner.OnActiveStateChanged();
                    }
                    else
                    {
                        _owner.SendState(this);
                    }
                }
                else if (wasEnabled && !Enabled)
                {
                    _owner.OnActiveStateChanged();
                }
            };
        }

        public bool Enabled { get; private set; }

        public (string Text, int Cursor, int Anchor)? Surrounding { get; private set; }

        public (uint Hint, uint Purpose)? ContentType { get; private set; }

        public Box? CursorRectangle { get; private set; }

        public bool Owns(Surface surface) => _resource.Client == surface.Resource.Client;

        public void SendEnter(Surface surface)
        {
            if (!_resource.IsDestroyed && !surface.IsDestroyed)
            {
                _resource.SendEnter(surface.Resource);
            }
        }

        public void SendLeave(Surface surface)
        {
            if (!_resource.IsDestroyed && !surface.IsDestroyed)
            {
                _resource.SendLeave(surface.Resource);
            }

            if (Enabled)
            {
                Enabled = false;
                _pendingEnabled = false;
                _owner.OnActiveStateChanged();
            }
        }

        public void ApplyPreedit(string? text, int cursorBegin, int cursorEnd)
        {
            if (!_resource.IsDestroyed)
            {
                _resource.SendPreeditString(text, cursorBegin, cursorEnd);
            }
        }

        public void ApplyCommit(string? text)
        {
            if (!_resource.IsDestroyed)
            {
                _resource.SendCommitString(text);
            }
        }

        public void ApplyDeleteSurrounding(uint before, uint after)
        {
            if (!_resource.IsDestroyed)
            {
                _resource.SendDeleteSurroundingText(before, after);
            }
        }

        public void ApplyDone()
        {
            if (!_resource.IsDestroyed)
            {
                _resource.SendDone(_serial);
            }
        }
    }
}
