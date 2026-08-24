using System.Text;
using Basin.Capabilities;
using Basin.Plasma.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class TextInputV2Manager : IDisposable
{
    public const int Version = 1;

    private static readonly byte[] ModifiersMap = Encoding.UTF8.GetBytes("Shift\0Control\0Mod1\0Mod4\0");

    private readonly WlGlobal _global;
    private readonly Basin.Seat.Seat? _seat;
    private readonly ITextInputMethod? _method;
    private readonly List<TextInput> _textInputs = [];
    private TextInput? _active;
    private Surface? _activeSurface;

    public TextInputV2Manager(WlServerDisplay display, Basin.Seat.Seat? seat, ITextInputMethod? method)
    {
        ArgumentNullException.ThrowIfNull(display);
        _seat = seat;
        _method = method;
        _global = display.CreateGlobal(ZwpTextInputManagerV2.Interface, Version, OnBind);
        if (_seat is { } withSeat)
        {
            withSeat.Keyboard.FocusChanged += OnFocusChanged;
            withSeat.Keyboard.KeymapChanged += OnKeymapChanged;
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
            withSeat.Keyboard.KeymapChanged -= OnKeymapChanged;
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

    private void OnFocusChanged(Surface? focus)
    {
        foreach (var textInput in _textInputs.ToArray())
        {
            textInput.UpdateFocus(focus);
        }

        Sync();
    }

    private void OnKeymapChanged()
    {
        foreach (var textInput in _textInputs)
        {
            textInput.ResendModifiersMap();
        }
    }

    private void Sync()
    {
        var focus = _seat?.Keyboard.Focus;
        var active = focus is null ? null : ActiveTextInput(focus);
        if (ReferenceEquals(active, _active))
        {
            return;
        }

        if (_active is not null && active is null)
        {
            if (_activeSurface is { } previous)
            {
                _method?.Deactivate(previous);
            }

            _active = null;
            _activeSurface = null;
            TextInputDeactivated?.Invoke();
            return;
        }

        _active = active;
        _activeSurface = focus;
        if (active is not null && focus is not null)
        {
            _method?.Activate(focus);
            SendState(active);
            TextInputActivated?.Invoke(focus);
        }
    }

    private TextInput? ActiveTextInput(Surface focus)
    {
        foreach (var textInput in _textInputs)
        {
            if (textInput.IsEnteredOn(focus) && textInput.IsEnabledFor(focus))
            {
                return textInput;
            }
        }

        return null;
    }

    private void OnPreedit(PreeditString preedit) => _active?.ApplyPreedit(preedit);

    private void OnCommitString(string text) => _active?.ApplyCommit(text);

    private void OnDeleteSurrounding(uint before, uint after) => _active?.ApplyDeleteSurrounding(before, after);

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

        if (textInput.CursorRectangle is { } rect)
        {
            method.CursorRectangle(rect);
        }

        method.Commit(0);
    }

    private void OnUpdateState(TextInput textInput, uint reason)
    {
        if (!ReferenceEquals(textInput, _active))
        {
            return;
        }

        SendState(textInput);
    }

    private uint NextSerial() => _seat?.NextSerial(Basin.Seat.SerialKind.Other) ?? 0;

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpTextInputManagerV2Resource(client, version, id);
        manager.GetTextInput += (_, e) =>
        {
            var resource = new ZwpTextInputV2Resource(client, manager.Version, e.Id);
            var textInput = new TextInput(this, resource);
            _textInputs.Add(textInput);
            resource.Destroyed += (_, _) =>
            {
                _textInputs.Remove(textInput);
                Sync();
            };

            if (_seat?.Keyboard.Focus is { } focus)
            {
                textInput.UpdateFocus(focus);
            }
        };
    }

    private static uint PurposeToV3(uint purpose) => purpose >= 9 ? purpose + 1 : purpose;

    private sealed class TextInput
    {
        private readonly TextInputV2Manager _owner;
        private readonly ZwpTextInputV2Resource _resource;
        private readonly HashSet<WlSurfaceResource> _enabled = [];
        private WlSurfaceResource? _entered;
        private uint _enterSerial;

        internal TextInput(TextInputV2Manager owner, ZwpTextInputV2Resource resource)
        {
            _owner = owner;
            _resource = resource;

            resource.Enable += (_, e) =>
            {
                if (e.Surface is { } surface && _enabled.Add(surface))
                {
                    _owner.Sync();
                }
            };
            resource.Disable += (_, e) =>
            {
                if (e.Surface is { } surface && _enabled.Remove(surface))
                {
                    _owner.Sync();
                }
            };
            resource.SetSurroundingText += (_, e) => Surrounding = (e.Text, e.Cursor, e.Anchor);
            resource.SetContentType += (_, e) => ContentType = ((uint)e.Hint, PurposeToV3((uint)e.Purpose));
            resource.SetCursorRectangle += (_, e) => CursorRectangle = new Box(e.X, e.Y, e.Width, e.Height);
            resource.UpdateState += (_, e) =>
            {
                if (_entered is null || e.Serial != _enterSerial)
                {
                    return;
                }

                _owner.OnUpdateState(this, (uint)e.Reason);
            };
        }

        internal (string Text, int Cursor, int Anchor)? Surrounding { get; private set; }

        internal (uint Hint, uint Purpose)? ContentType { get; private set; }

        internal Box? CursorRectangle { get; private set; }

        internal bool IsEnteredOn(Surface surface) => _entered is { } entered && ReferenceEquals(entered, surface.Resource);

        internal bool IsEnabledFor(Surface surface) => _enabled.Contains(surface.Resource);

        internal void UpdateFocus(Surface? focus)
        {
            if (_entered is { } entered && (focus is null || !ReferenceEquals(entered, focus.Resource)))
            {
                _entered = null;
                if (!_resource.IsDestroyed && !entered.IsDestroyed)
                {
                    _resource.SendLeave(_owner.NextSerial(), entered);
                }
            }

            if (focus is { } surface && _entered is null && !_resource.IsDestroyed &&
                _resource.Client == surface.Resource.Client && !surface.IsDestroyed)
            {
                _entered = surface.Resource;
                _enterSerial = _owner.NextSerial();
                _resource.SendEnter(_enterSerial, surface.Resource);
                _resource.SendModifiersMap(ModifiersMap);
                _resource.SendInputPanelState(ZwpTextInputV2.InputPanelVisibility.Hidden, 0, 0, 0, 0);
                _resource.SendTextDirection(ZwpTextInputV2.TextDirection.Auto);
            }
        }

        internal void ResendModifiersMap()
        {
            if (_entered is not null && !_resource.IsDestroyed)
            {
                _resource.SendModifiersMap(ModifiersMap);
            }
        }

        internal void ApplyPreedit(in PreeditString preedit)
        {
            if (_resource.IsDestroyed)
            {
                return;
            }

            var length = (uint)Encoding.UTF8.GetByteCount(preedit.Text);
            if (length > 0)
            {
                var style = preedit.CursorBegin != preedit.CursorEnd
                    ? ZwpTextInputV2.PreeditStyle.Highlight
                    : ZwpTextInputV2.PreeditStyle.Underline;
                _resource.SendPreeditStyling(0, length, style);
            }

            if (preedit.CursorBegin >= 0)
            {
                _resource.SendPreeditCursor(preedit.CursorBegin);
            }

            _resource.SendPreeditString(preedit.Text, string.Empty);
        }

        internal void ApplyCommit(string text)
        {
            if (!_resource.IsDestroyed)
            {
                _resource.SendCommitString(text);
            }
        }

        internal void ApplyDeleteSurrounding(uint before, uint after)
        {
            if (!_resource.IsDestroyed)
            {
                _resource.SendDeleteSurroundingText(before, after);
            }
        }
    }
}
