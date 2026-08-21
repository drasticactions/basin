using Basin.Backend.Wayland.Protocol;
using Basin.Capabilities;

namespace Basin.Backend.Wayland;

public sealed class WaylandSeamTextInput : ITextInputMethod, IDisposable
{
    private readonly WaylandBackend _backend;
    private readonly ZwpTextInputV3? _textInput;
    private Surface? _active;
    private bool _parentFocused;
    private bool _enabled;
    private bool _disposed;

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

    public Func<Surface, Box, (WaylandOutput Output, Box Rect)?>? LocateCursorRectangle { get; set; }

    public bool IsAvailable => !_disposed && _textInput is { IsDestroyed: false };

    public bool HasKeyboardGrab => false;

    public event Action<PreeditString>? Preedit;

    public event Action<string>? CommitString;

    public event Action<uint, uint>? DeleteSurroundingText;

    public event Action? Done;

    public event Action? AvailabilityChanged;

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

    public void Deactivate(Surface surface)
    {
        _active = null;
        if (Ready() is not { } textInput || !_enabled)
        {
            return;
        }

        _enabled = false;
        textInput.Disable();
        Push(textInput);
    }

    public void SurroundingText(string text, uint cursor, uint anchor)
    {
        if (Ready() is { } textInput && _enabled)
        {
            textInput.SetSurroundingText(text ?? string.Empty, (int)cursor, (int)anchor);
        }
    }

    public void ContentType(uint hint, uint purpose)
    {
        if (Ready() is { } textInput && _enabled)
        {
            textInput.SetContentType((ZwpTextInputV3.ContentHint)hint, (ZwpTextInputV3.ContentPurpose)purpose);
        }
    }

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

    public void Commit(uint serial)
    {
        if (Ready() is { } textInput)
        {
            Push(textInput);
        }
    }

    public void ForwardKey(uint timeMs, uint keycode, bool pressed)
    {
    }

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
