using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwp_text_input_manager_v1</c> global for older clients. A text input is active when its client activated it on the surface with keyboard focus.
/// </summary>
public sealed class TextInputV1Manager : IDisposable, IZwpTextInputManagerV1Handler
{
    /// <summary>
    /// The highest global version that the manager advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly Seat.Seat? _seat;
    private readonly ITextInputMethod? _method;
    private readonly List<TextInput> _textInputs = [];
    private Surface? _active;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextInputV1Manager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="seat">The seat whose keyboard focus selects the active text input, or <see langword="null"/>.</param>
    /// <param name="method">The input method that serves text input, or <see langword="null"/>.</param>
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
            live.AvailabilityChanged += OnAvailabilityChanged;
        }
    }

    /// <summary>
    /// Occurs when a text input on a surface becomes the active one.
    /// </summary>
    public event Action<Surface>? TextInputActivated;

    /// <summary>
    /// Occurs when no text input is active any more.
    /// </summary>
    public event Action? TextInputDeactivated;

    /// <summary>
    /// Stops following the seat and the input method, and removes the global.
    /// </summary>
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
            live.AvailabilityChanged -= OnAvailabilityChanged;
        }

        _global.Dispose();
    }

    private void OnFocusChanged(Surface? surface) => Sync();

    private void OnAvailabilityChanged()
    {
        var before = _active;
        Sync();
        if (ReferenceEquals(_active, before) && _active is { } surface && ActiveTextInput() is { } active)
        {
            _method?.Activate(surface);
            SendState(active);
        }
    }

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
            method.CursorRectangle(ServerScaling.ToLogicalNearest(rect, _active?.ServerScale ?? 1.0));
        }

        method.Commit(textInput.Serial);
    }

    void IZwpTextInputManagerV1Handler.CreateTextInput(ZwpTextInputManagerV1Resource resource, in ZwpTextInputManagerV1Resource.CreateTextInputEventArgs args) =>
        _textInputs.Add(new TextInput(this, resource.Client, resource.Version, args.Id));

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new ZwpTextInputManagerV1Resource(client, version, id, this);

    private static uint PurposeToV3(uint purpose) => purpose >= 9 ? purpose + 1 : purpose;

    private sealed class TextInput : IZwpTextInputV1Handler
    {
        private readonly TextInputV1Manager _owner;
        private readonly ZwpTextInputV1Resource _resource;
        private WlSurfaceResource? _activated;
        private bool _entered;

        internal TextInput(TextInputV1Manager owner, WlClient client, uint version, uint id)
        {
            _owner = owner;
            _resource = new ZwpTextInputV1Resource(client, version, id, this);
            _resource.Destroyed += (_, _) =>
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

        void IZwpTextInputV1Handler.Activate(ZwpTextInputV1Resource resource, in ZwpTextInputV1Resource.ActivateEventArgs args)
        {
            _activated = args.Surface;
            _owner.Sync();
        }

        void IZwpTextInputV1Handler.Deactivate(ZwpTextInputV1Resource resource, in ZwpTextInputV1Resource.DeactivateEventArgs args)
        {
            _activated = null;
            _owner.Sync();
        }

        void IZwpTextInputV1Handler.ShowInputPanel(ZwpTextInputV1Resource resource)
        {
        }

        void IZwpTextInputV1Handler.HideInputPanel(ZwpTextInputV1Resource resource)
        {
        }

        void IZwpTextInputV1Handler.Reset(ZwpTextInputV1Resource resource)
        {
        }

        void IZwpTextInputV1Handler.SetSurroundingText(ZwpTextInputV1Resource resource, in ZwpTextInputV1Resource.SetSurroundingTextEventArgs args) =>
            Surrounding = (args.Text, args.Cursor, args.Anchor);

        void IZwpTextInputV1Handler.SetContentType(ZwpTextInputV1Resource resource, in ZwpTextInputV1Resource.SetContentTypeEventArgs args) =>
            ContentType = ((uint)args.Hint, PurposeToV3((uint)args.Purpose));

        void IZwpTextInputV1Handler.SetCursorRectangle(ZwpTextInputV1Resource resource, in ZwpTextInputV1Resource.SetCursorRectangleEventArgs args) =>
            CursorRectangle = new Box(args.X, args.Y, args.Width, args.Height);

        void IZwpTextInputV1Handler.SetPreferredLanguage(ZwpTextInputV1Resource resource, in ZwpTextInputV1Resource.SetPreferredLanguageEventArgs args)
        {
        }

        void IZwpTextInputV1Handler.CommitState(ZwpTextInputV1Resource resource, in ZwpTextInputV1Resource.CommitStateEventArgs args)
        {
            Serial = args.Serial;
            if (IsActive)
            {
                _owner.SendState(this);
            }
        }

        void IZwpTextInputV1Handler.InvokeAction(ZwpTextInputV1Resource resource, in ZwpTextInputV1Resource.InvokeActionEventArgs args)
        {
        }
    }
}
