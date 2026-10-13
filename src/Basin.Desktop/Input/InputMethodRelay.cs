using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class InputMethodRelay : ITextInputMethod, IInputMethodClients, IDisposable
{
    public const int Version = 1;

    private readonly WlServerDisplay _display;
    private readonly WlGlobal _global;
    private readonly Seat.Seat? _seat;
    private ZwpInputMethodV2Resource? _method;
    private ZwpInputMethodKeyboardGrabV2Resource? _grab;
    private string? _pendingPreedit;
    private int _preeditBegin;
    private int _preeditEnd;
    private string? _pendingCommit;
    private (uint Before, uint After)? _pendingDelete;
    private Surface? _activeSurface;
    private readonly ITextInputMethod? _fallback;

    public InputMethodRelay(WlServerDisplay display, Seat.Seat? seat)
        : this(display, seat, fallback: null)
    {
    }

    public InputMethodRelay(WlServerDisplay display, Seat.Seat? seat, ITextInputMethod? fallback)
    {
        ArgumentNullException.ThrowIfNull(display);
        _display = display;
        _seat = seat;
        _fallback = fallback;
        _global = display.CreateGlobal(ZwpInputMethodManagerV2.Interface, Version, OnBind);
        if (seat is { } withSeat)
        {
            withSeat.Keyboard.KeymapChanged += OnKeymapChanged;
        }

        if (fallback is not null)
        {
            fallback.Preedit += OnFallbackPreedit;
            fallback.CommitString += OnFallbackCommitString;
            fallback.DeleteSurroundingText += OnFallbackDeleteSurroundingText;
            fallback.Done += OnFallbackDone;
            fallback.AvailabilityChanged += OnFallbackAvailabilityChanged;
        }
    }

    public ITextInputMethod? Fallback => _fallback;

    public bool HasInputMethodClient => _method is { IsDestroyed: false };

    private void OnFallbackPreedit(PreeditString preedit)
    {
        if (!HasInputMethodClient)
        {
            Preedit?.Invoke(preedit);
        }
    }

    private void OnFallbackCommitString(string text)
    {
        if (!HasInputMethodClient)
        {
            CommitString?.Invoke(text);
        }
    }

    private void OnFallbackDeleteSurroundingText(uint before, uint after)
    {
        if (!HasInputMethodClient)
        {
            DeleteSurroundingText?.Invoke(before, after);
        }
    }

    private void OnFallbackDone()
    {
        if (!HasInputMethodClient)
        {
            Done?.Invoke();
        }
    }

    private void OnFallbackAvailabilityChanged() => AvailabilityChanged?.Invoke();

    private void OnKeymapChanged()
    {
        if (_grab is not { IsDestroyed: false } grab || _seat is not { } seat)
        {
            return;
        }

        if (seat.Keyboard.KeymapFor(grab.Client) is { } keymap)
        {
            grab.SendKeymap(WlKeyboard.KeymapFormat.XkbV1, keymap.Fd, keymap.Size);
        }

        ForwardSeatModifiers();
    }

    public bool IsAvailable => HasInputMethodClient || (_fallback?.IsAvailable ?? false);

    public bool HasKeyboardGrab => _grab is { IsDestroyed: false };

    public bool IsInputMethodClient(object? client) =>
        client is WlClient wlClient && _method is { IsDestroyed: false } method &&
        ReferenceEquals(method.Client, wlClient);

    public event Action<PreeditString>? Preedit;

    public event Action<string>? CommitString;

    public event Action<uint, uint>? DeleteSurroundingText;

    public event Action? Done;

    public event Action? AvailabilityChanged;

    public void Dispose()
    {
        if (_seat is { } seat)
        {
            seat.Keyboard.KeymapChanged -= OnKeymapChanged;
        }

        if (_fallback is { } fallback)
        {
            fallback.Preedit -= OnFallbackPreedit;
            fallback.CommitString -= OnFallbackCommitString;
            fallback.DeleteSurroundingText -= OnFallbackDeleteSurroundingText;
            fallback.Done -= OnFallbackDone;
            fallback.AvailabilityChanged -= OnFallbackAvailabilityChanged;
        }

        _global.Dispose();
    }

    public void Activate(Surface surface)
    {
        _fallback?.Activate(surface);
        if (ReferenceEquals(_activeSurface, surface))
        {
            return;
        }

        _activeSurface = surface;
        if (_method is { IsDestroyed: false } method)
        {
            method.SendActivate();
        }
    }

    public void Deactivate(Surface surface)
    {
        _fallback?.Deactivate(surface);
        if (!ReferenceEquals(_activeSurface, surface))
        {
            return;
        }

        _activeSurface = null;
        if (_method is { IsDestroyed: false } method)
        {
            method.SendDeactivate();
            method.SendDone();
        }
    }

    public void SurroundingText(string text, uint cursor, uint anchor)
    {
        _fallback?.SurroundingText(text, cursor, anchor);
        if (_method is { IsDestroyed: false } method)
        {
            method.SendSurroundingText(text, cursor, anchor);
        }
    }

    public void ContentType(uint hint, uint purpose)
    {
        _fallback?.ContentType(hint, purpose);
        if (_method is { IsDestroyed: false } method)
        {
            method.SendContentType((ZwpTextInputV3.ContentHint)hint, (ZwpTextInputV3.ContentPurpose)purpose);
        }
    }

    public void CursorRectangle(in Box rect)
    {
        _fallback?.CursorRectangle(rect);
    }

    public void Commit(uint serial)
    {
        _fallback?.Commit(serial);
        if (_method is { IsDestroyed: false } method)
        {
            method.SendDone();
        }
    }

    public void ForwardKey(uint timeMs, uint keycode, bool pressed)
    {
        if (_grab is { IsDestroyed: false } grab)
        {
            grab.SendKey(
                _display.NextSerial(),
                timeMs,
                keycode,
                pressed ? WlKeyboard.KeyState.Pressed : WlKeyboard.KeyState.Released);
        }
    }

    public void ForwardModifiers(uint depressed, uint latched, uint locked, uint group)
    {
        if (_grab is { IsDestroyed: false } grab)
        {
            grab.SendModifiers(_display.NextSerial(), depressed, latched, locked, group);
        }
    }

    public void ForwardSeatModifiers()
    {
        if (_seat is { } seat)
        {
            var modifiers = seat.Keyboard.ModifierState;
            ForwardModifiers(modifiers.Depressed, modifiers.Latched, modifiers.Locked, modifiers.Group);
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpInputMethodManagerV2Resource(client, version, id);
        manager.GetInputMethod += (_, e) =>
        {
            var resource = new ZwpInputMethodV2Resource(client, manager.Version, e.InputMethod);
            if (HasInputMethodClient)
            {
                resource.SendUnavailable();
                return;
            }

            _method = resource;
            if (_activeSurface is not null)
            {
                resource.SendActivate();
            }

            resource.Destroyed += (_, _) =>
            {
                if (_method == resource)
                {
                    _method = null;
                    AvailabilityChanged?.Invoke();
                }
            };

            resource.SetPreeditString += (_, pe) => (_pendingPreedit, _preeditBegin, _preeditEnd) = (pe.Text, pe.CursorBegin, pe.CursorEnd);
            resource.CommitString += (_, ce) => _pendingCommit = ce.Text;
            resource.DeleteSurroundingText += (_, de) => _pendingDelete = (de.BeforeLength, de.AfterLength);
            resource.Commit += (_, _) =>
            {
                if (_pendingPreedit is { } preedit)
                {
                    Preedit?.Invoke(new PreeditString(preedit, _preeditBegin, _preeditEnd));
                }

                if (_pendingCommit is { } committed)
                {
                    CommitString?.Invoke(committed);
                }

                if (_pendingDelete is { } delete)
                {
                    DeleteSurroundingText?.Invoke(delete.Before, delete.After);
                }

                _pendingPreedit = null;
                _pendingCommit = null;
                _pendingDelete = null;
                Done?.Invoke();
            };
            resource.GrabKeyboard += (_, ge) =>
            {
                var grab = new ZwpInputMethodKeyboardGrabV2Resource(resource.Client, resource.Version, ge.Keyboard);
                _grab = grab;
                grab.Release += (_, _) => _grab = null;
                grab.Destroyed += (_, _) =>
                {
                    if (_grab == grab)
                    {
                        _grab = null;
                    }
                };

                if (_seat is { } seat)
                {
                    if (seat.Keyboard.KeymapFor(grab.Client) is { } keymap)
                    {
                        grab.SendKeymap(WlKeyboard.KeymapFormat.XkbV1, keymap.Fd, keymap.Size);
                    }

                    var repeat = seat.Keyboard.RepeatInfo;
                    grab.SendRepeatInfo(repeat.Rate, repeat.Delay);
                }

                ForwardSeatModifiers();
            };
            resource.GetInputPopupSurface += (_, pe) =>
            {
                _ = new ZwpInputPopupSurfaceV2Resource(resource.Client, resource.Version, pe.Id);
            };

            AvailabilityChanged?.Invoke();
        };
    }
}
