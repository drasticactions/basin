using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwp_input_method_manager_v2</c> global and relays text input to one input method client. While no client is bound, a fallback input method can serve instead.
/// </summary>
public sealed class InputMethodRelay : ITextInputMethod, IInputMethodClients, IDisposable, IZwpInputMethodManagerV2Handler, IZwpInputMethodV2Handler, IZwpInputMethodKeyboardGrabV2Handler
{
    /// <summary>
    /// The highest global version that the relay advertises.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="InputMethodRelay"/> class with no fallback, and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="seat">The seat whose keymap and modifiers go to the keyboard grab, or <see langword="null"/>.</param>
    public InputMethodRelay(WlServerDisplay display, Seat.Seat? seat)
        : this(display, seat, fallback: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InputMethodRelay"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="seat">The seat whose keymap and modifiers go to the keyboard grab, or <see langword="null"/>.</param>
    /// <param name="fallback">The input method that receives every request too. Its events pass through only while no client is bound.</param>
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

    /// <summary>
    /// Gets the fallback input method. It is <see langword="null"/> when none was given.
    /// </summary>
    public ITextInputMethod? Fallback => _fallback;

    /// <summary>
    /// Gets a value indicating whether a client input method is bound. Only one can be bound, and a second one receives <c>unavailable</c>.
    /// </summary>
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

    /// <inheritdoc/>
    public bool IsAvailable => HasInputMethodClient || (_fallback?.IsAvailable ?? false);

    /// <inheritdoc/>
    public bool HasKeyboardGrab => _grab is { IsDestroyed: false };

    /// <inheritdoc/>
    public bool IsInputMethodClient(object? client) =>
        client is WlClient wlClient && _method is { IsDestroyed: false } method &&
        ReferenceEquals(method.Client, wlClient);

    /// <inheritdoc/>
    public event Action<PreeditString>? Preedit;

    /// <inheritdoc/>
    public event Action<string>? CommitString;

    /// <inheritdoc/>
    public event Action<uint, uint>? DeleteSurroundingText;

    /// <inheritdoc/>
    public event Action? Done;

    /// <inheritdoc/>
    public event Action? AvailabilityChanged;

    /// <summary>
    /// Stops following the seat and the fallback, and removes the global.
    /// </summary>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void SurroundingText(string text, uint cursor, uint anchor)
    {
        _fallback?.SurroundingText(text, cursor, anchor);
        if (_method is { IsDestroyed: false } method)
        {
            method.SendSurroundingText(text, cursor, anchor);
        }
    }

    /// <inheritdoc/>
    public void ContentType(uint hint, uint purpose)
    {
        _fallback?.ContentType(hint, purpose);
        if (_method is { IsDestroyed: false } method)
        {
            method.SendContentType((ZwpTextInputV3.ContentHint)hint, (ZwpTextInputV3.ContentPurpose)purpose);
        }
    }

    /// <summary>
    /// Passes the text cursor area to the fallback only. The input method protocol has no request for it.
    /// </summary>
    /// <param name="rect">The cursor area in surface-local coordinates.</param>
    public void CursorRectangle(in Box rect)
    {
        _fallback?.CursorRectangle(rect);
    }

    /// <inheritdoc/>
    public void Commit(uint serial)
    {
        _fallback?.Commit(serial);
        if (_method is { IsDestroyed: false } method)
        {
            method.SendDone();
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void ForwardModifiers(uint depressed, uint latched, uint locked, uint group)
    {
        if (_grab is { IsDestroyed: false } grab)
        {
            grab.SendModifiers(_display.NextSerial(), depressed, latched, locked, group);
        }
    }

    /// <summary>
    /// Sends the current modifier state of the seat to the keyboard grab. It does nothing without a seat or a grab.
    /// </summary>
    public void ForwardSeatModifiers()
    {
        if (_seat is { } seat)
        {
            var modifiers = seat.Keyboard.ModifierState;
            ForwardModifiers(modifiers.Depressed, modifiers.Latched, modifiers.Locked, modifiers.Group);
        }
    }

    void IZwpInputMethodManagerV2Handler.Destroy(ZwpInputMethodManagerV2Resource resource)
    {
    }

    void IZwpInputMethodManagerV2Handler.GetInputMethod(ZwpInputMethodManagerV2Resource resource, in ZwpInputMethodManagerV2Resource.GetInputMethodEventArgs args)
    {
        if (HasInputMethodClient)
        {
            var unavailable = new ZwpInputMethodV2Resource(resource.Client, resource.Version, args.InputMethod, UnavailableMethod.Instance);
            unavailable.SendUnavailable();
            return;
        }

        var method = new ZwpInputMethodV2Resource(resource.Client, resource.Version, args.InputMethod, this);
        _method = method;
        if (_activeSurface is not null)
        {
            method.SendActivate();
        }

        method.Destroyed += (_, _) =>
        {
            if (_method == method)
            {
                _method = null;
                AvailabilityChanged?.Invoke();
            }
        };

        AvailabilityChanged?.Invoke();
    }

    void IZwpInputMethodV2Handler.SetPreeditString(ZwpInputMethodV2Resource resource, in ZwpInputMethodV2Resource.SetPreeditStringEventArgs args) =>
        (_pendingPreedit, _preeditBegin, _preeditEnd) = (args.Text, args.CursorBegin, args.CursorEnd);

    void IZwpInputMethodV2Handler.CommitString(ZwpInputMethodV2Resource resource, in ZwpInputMethodV2Resource.CommitStringEventArgs args) =>
        _pendingCommit = args.Text;

    void IZwpInputMethodV2Handler.DeleteSurroundingText(ZwpInputMethodV2Resource resource, in ZwpInputMethodV2Resource.DeleteSurroundingTextEventArgs args) =>
        _pendingDelete = (args.BeforeLength, args.AfterLength);

    void IZwpInputMethodV2Handler.Commit(ZwpInputMethodV2Resource resource, in ZwpInputMethodV2Resource.CommitEventArgs args)
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
    }

    void IZwpInputMethodV2Handler.GrabKeyboard(ZwpInputMethodV2Resource resource, in ZwpInputMethodV2Resource.GrabKeyboardEventArgs args)
    {
        var grab = new ZwpInputMethodKeyboardGrabV2Resource(resource.Client, resource.Version, args.Keyboard, this);
        _grab = grab;
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
    }

    void IZwpInputMethodV2Handler.GetInputPopupSurface(ZwpInputMethodV2Resource resource, in ZwpInputMethodV2Resource.GetInputPopupSurfaceEventArgs args) =>
        _ = new ZwpInputPopupSurfaceV2Resource(resource.Client, resource.Version, args.Id);

    void IZwpInputMethodV2Handler.Destroy(ZwpInputMethodV2Resource resource)
    {
    }

    void IZwpInputMethodKeyboardGrabV2Handler.Release(ZwpInputMethodKeyboardGrabV2Resource resource) => _grab = null;

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new ZwpInputMethodManagerV2Resource(client, version, id, this);

    private sealed class UnavailableMethod : IZwpInputMethodV2Handler
    {
        public static readonly UnavailableMethod Instance = new();

        void IZwpInputMethodV2Handler.CommitString(ZwpInputMethodV2Resource resource, in ZwpInputMethodV2Resource.CommitStringEventArgs args)
        {
        }

        void IZwpInputMethodV2Handler.SetPreeditString(ZwpInputMethodV2Resource resource, in ZwpInputMethodV2Resource.SetPreeditStringEventArgs args)
        {
        }

        void IZwpInputMethodV2Handler.DeleteSurroundingText(ZwpInputMethodV2Resource resource, in ZwpInputMethodV2Resource.DeleteSurroundingTextEventArgs args)
        {
        }

        void IZwpInputMethodV2Handler.Commit(ZwpInputMethodV2Resource resource, in ZwpInputMethodV2Resource.CommitEventArgs args)
        {
        }

        void IZwpInputMethodV2Handler.GetInputPopupSurface(ZwpInputMethodV2Resource resource, in ZwpInputMethodV2Resource.GetInputPopupSurfaceEventArgs args)
        {
        }

        void IZwpInputMethodV2Handler.GrabKeyboard(ZwpInputMethodV2Resource resource, in ZwpInputMethodV2Resource.GrabKeyboardEventArgs args)
        {
        }

        void IZwpInputMethodV2Handler.Destroy(ZwpInputMethodV2Resource resource)
        {
        }
    }
}
