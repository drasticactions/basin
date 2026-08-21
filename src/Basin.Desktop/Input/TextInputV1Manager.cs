using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class TextInputV1Manager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly Seat.Seat? _seat;
    private readonly ITextInputMethod? _method;
    private readonly List<TextInput> _textInputs = [];
    private Surface? _active;

    public TextInputV1Manager(WlServerDisplay display, Seat.Seat? seat, ITextInputMethod? method)
    {
        ArgumentNullException.ThrowIfNull(display);
        _seat = seat;
        _method = method;
        _global = display.CreateGlobal(ZwpTextInputManagerV1.Interface, Version, OnBind);
        if (_seat is { } withSeat)
        {
            withSeat.Keyboard.FocusChanged += OnFocusChanged;
        }

        if (_method is { } live)
        {
            live.Preedit += OnPreedit;
            live.CommitString += OnCommitString;
            live.DeleteSurroundingText += OnDeleteSurrounding;
            live.AvailabilityChanged += Sync;
        }
    }

    public event Action<Surface>? TextInputActivated;

    public event Action? TextInputDeactivated;

    public void Dispose()
    {
        if (_seat is { } withSeat)
        {
            withSeat.Keyboard.FocusChanged -= OnFocusChanged;
        }

        if (_method is { } live)
        {
            live.Preedit -= OnPreedit;
            live.CommitString -= OnCommitString;
            live.DeleteSurroundingText -= OnDeleteSurrounding;
            live.AvailabilityChanged -= Sync;
        }

        _global.Dispose();
    }

    private void OnFocusChanged(Surface? surface) => Sync();

    private void Sync()
    {
        var focus = _seat?.Keyboard.Focus;
        foreach (var textInput in _textInputs)
        {
            textInput.UpdateFocus(focus);
        }

        var active = ActiveTextInput();
        var surface = active is null ? null : focus;
        if (ReferenceEquals(surface, _active))
        {
            return;
        }

        if (_active is { } previous)
        {
            _method?.Deactivate(previous);
        }

        _active = surface;
        if (surface is not null)
        {
            _method?.Activate(surface);
            SendState(active!);
            TextInputActivated?.Invoke(surface);
        }
        else
        {
            TextInputDeactivated?.Invoke();
        }
    }

    private TextInput? ActiveTextInput()
    {
        foreach (var textInput in _textInputs)
        {
            if (textInput.IsActive)
            {
                return textInput;
            }
        }

        return null;
    }

    private void OnPreedit(PreeditString preedit) => ActiveTextInput()?.ApplyPreedit(preedit);

    private void OnCommitString(string text) => ActiveTextInput()?.ApplyCommit(text);

    private void OnDeleteSurrounding(uint before, uint after) =>
        ActiveTextInput()?.ApplyDeleteSurrounding(before, after);

    private void SendState(TextInput textInput)
    {
        if (_method is not { } method)
        {
            return;
        }

        if (textInput.Surrounding is { } surrounding)
        {
            method.SurroundingText(surrounding.Text, surrounding.Cursor, surrounding.Anchor);
        }

        if (textInput.ContentType is { } contentType)
        {
            method.ContentType(contentType.Hint, contentType.Purpose);
        }

        if (textInput.CursorRectangle is { } rect)
        {
            method.CursorRectangle(rect);
        }

        method.Commit(textInput.Serial);
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpTextInputManagerV1Resource(client, version, id);
        manager.CreateTextInput += (_, e) =>
        {
            var resource = new ZwpTextInputV1Resource(client, manager.Version, e.Id);
            _textInputs.Add(new TextInput(this, resource));
        };
    }

    private static uint PurposeToV3(uint purpose) => purpose >= 9 ? purpose + 1 : purpose;

    private sealed class TextInput
    {
        private readonly TextInputV1Manager _owner;
        private readonly ZwpTextInputV1Resource _resource;
        private WlSurfaceResource? _activated;
        private bool _entered;

        internal TextInput(TextInputV1Manager owner, ZwpTextInputV1Resource resource)
        {
            _owner = owner;
            _resource = resource;

            resource.Activate += (_, e) =>
            {
                _activated = e.Surface;
                _owner.Sync();
            };
            resource.Deactivate += (_, _) =>
            {
                _activated = null;
                _owner.Sync();
            };
            resource.SetSurroundingText += (_, e) => Surrounding = (e.Text, e.Cursor, e.Anchor);
            resource.SetContentType += (_, e) => ContentType = ((uint)e.Hint, PurposeToV3((uint)e.Purpose));
            resource.SetCursorRectangle += (_, e) => CursorRectangle = new Box(e.X, e.Y, e.Width, e.Height);
            resource.CommitState += (_, e) =>
            {
                Serial = e.Serial;
                if (IsActive)
                {
                    _owner.SendState(this);
                }
            };
            resource.Destroyed += (_, _) =>
            {
                _activated = null;
                _entered = false;
                _owner._textInputs.Remove(this);
                _owner.Sync();
            };
        }

        internal bool IsActive => _entered;

        internal uint Serial { get; private set; }

        internal (string Text, uint Cursor, uint Anchor)? Surrounding { get; private set; }

        internal (uint Hint, uint Purpose)? ContentType { get; private set; }

        internal Box? CursorRectangle { get; private set; }

        internal void UpdateFocus(Surface? focus)
        {
            var focused = _activated is not null && focus is { } surface && ReferenceEquals(surface.Resource, _activated);
            if (focused == _entered)
            {
                return;
            }

            _entered = focused;
            if (_resource.IsDestroyed)
            {
                return;
            }

            if (focused)
            {
                _resource.SendEnter(_activated!);
            }
            else
            {
                _resource.SendLeave();
            }
        }

        internal void ApplyPreedit(in PreeditString preedit)
        {
            if (_resource.IsDestroyed)
            {
                return;
            }

            _resource.SendPreeditCursor(preedit.CursorBegin);

            _resource.SendPreeditString(Serial, preedit.Text, string.Empty);
        }

        internal void ApplyCommit(string text)
        {
            if (!_resource.IsDestroyed)
            {
                _resource.SendCommitString(Serial, text);
            }
        }

        internal void ApplyDeleteSurrounding(uint before, uint after)
        {
            if (!_resource.IsDestroyed)
            {
                _resource.SendDeleteSurroundingText(-(int)before, before + after);
            }
        }
    }
}
