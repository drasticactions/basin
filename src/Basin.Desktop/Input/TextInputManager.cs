using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class TextInputManager : IDisposable
{
    public const int TextInputVersion = 1;

    private readonly WlGlobal _textInputGlobal;
    private readonly Seat.Seat? _seat;
    private readonly ITextInputMethod? _method;
    private readonly List<TextInput> _textInputs = [];
    private Surface? _focus;

    public TextInputManager(WlServerDisplay display, Seat.Seat? seat, ITextInputMethod? method)
    {
        ArgumentNullException.ThrowIfNull(display);
        _seat = seat;
        _method = method;
        _textInputGlobal = display.CreateGlobal(ZwpTextInputManagerV3.Interface, TextInputVersion, OnBindTextInput);
        if (_seat is { } withSeat)
        {
            withSeat.Keyboard.ModifiersChanged += OnModifiersChanged;
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

    public event Action<Surface>? TextInputActivated;

    public event Action? TextInputDeactivated;

    public void Dispose()
    {
        if (_seat is { } withSeat)
        {
            withSeat.Keyboard.ModifiersChanged -= OnModifiersChanged;
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

    public bool HasKeyboardGrab => _method?.HasKeyboardGrab ?? false;

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

    private void OnCommitString(string text) => ActiveTextInput()?.ApplyCommit(text);

    private void OnDeleteSurrounding(uint before, uint after) =>
        ActiveTextInput()?.ApplyDeleteSurrounding(before, after);

    private void OnMethodDone() => ActiveTextInput()?.ApplyDone();

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
            method.CursorRectangle(caret);
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
                CursorRectangle = _pendingCursorRectangle;

                var active = _owner.ActiveTextInput();
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
