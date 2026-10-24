using System.Text;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using Basin.Capabilities;

namespace Basin.Avalonia;

/// <summary>
/// An <see cref="ITextInputMethod"/> that uses the host platform input method through Avalonia.
/// Preedit and commit text from the host IME reach the compositor through the post callback.
/// </summary>
public sealed class AvaloniaTextInput : ITextInputMethod, IDisposable
{
    private readonly Action<Action> _post;
    private Surface? _active;
    private Box _cursorRect;
    private string _surrounding = string.Empty;
    private uint _surroundingCursor;
    private uint _surroundingAnchor;
    private bool _disposed;

    internal ImeClient Client { get; }

    internal BasinToplevelView? ActiveWindow { get; private set; }

    private BasinToplevelView? _view;
    private BasinToplevelView? _activeView;
    private bool _clientActive;
    private bool _shown;

    /// <summary>
    /// Makes a single view the host control that receives IME focus. Host text input on that view commits to the active text input.
    /// </summary>
    /// <param name="view">The view that shows the compositor.</param>
    public void AttachView(BasinToplevelView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
        view.TextInputMethodClientRequested += (_, e) =>
        {
            if (ReferenceEquals(_activeView, view))
            {
                e.Client = Client;
            }
        };
        view.TextInput += (_, e) =>
        {
            if (ReferenceEquals(_activeView, view) && !string.IsNullOrEmpty(e.Text))
            {
                CommitFromHost(e.Text!);
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaTextInput"/> class.
    /// </summary>
    /// <param name="postToCompositor">Runs work on the compositor thread. The events are raised from that work.</param>
    public AvaloniaTextInput(Action<Action> postToCompositor)
    {
        ArgumentNullException.ThrowIfNull(postToCompositor);
        _post = postToCompositor;
        Client = new ImeClient(this);
    }

    /// <summary>
    /// Gets a value indicating whether an input method can serve text input now. It is always <see langword="true"/>.
    /// </summary>
    public bool IsAvailable => true;

    /// <summary>
    /// Gets a value indicating whether the input method grabs the keyboard. It is always <see langword="false"/>, because the host IME sees keys first.
    /// </summary>
    public bool HasKeyboardGrab => false;

    /// <inheritdoc/>
    public event Action<PreeditString>? Preedit;

    /// <inheritdoc/>
    public event Action<string>? CommitString;

    /// <summary>
    /// Occurs when the input method asks to delete text. This implementation never raises it.
    /// </summary>
    public event Action<uint, uint>? DeleteSurroundingText
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <inheritdoc/>
    public event Action? Done;

    /// <summary>
    /// Occurs when <see cref="IsAvailable"/> changes. This implementation never raises it.
    /// </summary>
    public event Action? AvailabilityChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    internal event Action<BasinToplevelView?>? ActiveWindowChanged;

    internal Func<Surface, int>? IdResolver { get; set; }

    internal Func<int, BasinToplevelView?>? UiWindowResolver { get; set; }

    /// <inheritdoc/>
    public void Activate(Surface surface)
    {
        _active = surface;
        if (_view is { } view)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _clientActive = true;
                OfferClient(view, true);
            });
            return;
        }

        var id = IdResolver?.Invoke(surface) ?? 0;
        Dispatcher.UIThread.Post(() =>
        {
            var window = id != 0 ? UiWindowResolver?.Invoke(id) : null;
            ActiveWindow = window;
            Client.SetVisual(window);
            ActiveWindowChanged?.Invoke(window);
            if (window is not null)
            {
                RequeryIme(window);
            }
        });
    }

    /// <inheritdoc/>
    public void Deactivate(Surface surface)
    {
        if (!ReferenceEquals(_active, surface))
        {
            return;
        }

        _active = null;
        if (_view is { } view)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _clientActive = false;
                OfferClient(view, _shown);
            });
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var window = ActiveWindow;
            ActiveWindow = null;
            Client.SetVisual(null);
            ActiveWindowChanged?.Invoke(null);
            if (window is not null)
            {
                RequeryIme(window);
            }
        });
    }

    /// <summary>
    /// Offers or withdraws IME focus on the attached view, so that the host shows or hides its soft keyboard.
    /// It does nothing without an attached view.
    /// </summary>
    /// <param name="show"><see langword="true"/> to show the keyboard.</param>
    public void ShowSoftKeyboard(bool show)
    {
        if (_view is not { } view)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _shown = show;
            OfferClient(view, show || _clientActive);
        });
    }

    private void OfferClient(BasinToplevelView view, bool offer)
    {
        _activeView = offer ? view : null;
        Client.SetVisual(offer ? view : null);
        RequeryIme(view);
    }

    private static void RequeryIme(BasinToplevelView view) =>
        view.RaiseEvent(new TextInputMethodClientRequeryRequestedEventArgs
        {
            RoutedEvent = InputMethod.TextInputMethodClientRequeryRequestedEvent,
        });

    /// <inheritdoc/>
    public void SurroundingText(string text, uint cursor, uint anchor)
    {
        _surrounding = text;
        _surroundingCursor = cursor;
        _surroundingAnchor = anchor;
    }

    /// <summary>
    /// Receives the content type. This implementation ignores it.
    /// </summary>
    /// <param name="hint">The <c>zwp_text_input_v3.content_hint</c> bits.</param>
    /// <param name="purpose">The <c>zwp_text_input_v3.content_purpose</c> value.</param>
    public void ContentType(uint hint, uint purpose)
    {
    }

    /// <inheritdoc/>
    public void CursorRectangle(in Box rect) => _cursorRect = rect;

    /// <summary>
    /// Sends the last cursor area and surrounding text to the host IME on the UI thread.
    /// </summary>
    /// <param name="serial">The number of commits that the text input made. It is not used.</param>
    public void Commit(uint serial)
    {
        var rect = _cursorRect;
        var surrounding = _surrounding;
        var cursor = (int)_surroundingCursor;
        var anchor = (int)_surroundingAnchor;
        Dispatcher.UIThread.Post(() => Client.UpdateContext(rect, surrounding, cursor, anchor));
    }

    /// <summary>
    /// Receives a key for a keyboard grab. This implementation has no grab and ignores it.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    public void ForwardKey(uint timeMs, uint keycode, bool pressed)
    {
    }

    /// <summary>
    /// Receives the modifier state for a keyboard grab. This implementation has no grab and ignores it.
    /// </summary>
    /// <param name="depressed">The mask of modifiers that are held down.</param>
    /// <param name="latched">The mask of latched modifiers.</param>
    /// <param name="locked">The mask of locked modifiers.</param>
    /// <param name="group">The effective layout index.</param>
    public void ForwardModifiers(uint depressed, uint latched, uint locked, uint group)
    {
    }

    internal bool IsActiveOn(BasinToplevelView window) => ReferenceEquals(ActiveWindow, window);

    internal void CommitFromHost(string text)
    {
        _post(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (_active is null)
            {
                CommitString?.Invoke(text);
                return;
            }

            Preedit?.Invoke(new PreeditString(string.Empty, 0, 0));
            CommitString?.Invoke(text);
            Done?.Invoke();
        });
    }

    internal void PreeditFromHost(string? text, int? cursorPos)
    {
        _post(() =>
        {
            if (_disposed || _active is null)
            {
                return;
            }

            var preedit = text ?? string.Empty;
            var caret = Math.Clamp(cursorPos ?? preedit.Length, 0, preedit.Length);
            var byteOffset = Encoding.UTF8.GetByteCount(preedit.AsSpan(0, caret));
            Preedit?.Invoke(new PreeditString(preedit, byteOffset, byteOffset));
            Done?.Invoke();
        });
    }

    /// <summary>
    /// Stops delivery of host IME text to the compositor. Work that is already posted then does nothing.
    /// </summary>
    public void Dispose() => _disposed = true;

    internal sealed class ImeClient : TextInputMethodClient
    {
        private readonly AvaloniaTextInput _owner;
        private Visual? _visual;
        private Rect _cursorRect = new(0, 0, 1, 16);
        private string _surrounding = string.Empty;
        private TextSelection _selection;

        internal ImeClient(AvaloniaTextInput owner) => _owner = owner;

        internal void SetVisual(Visual? visual)
        {
            if (!ReferenceEquals(_visual, visual))
            {
                _visual = visual;
                RaiseTextViewVisualChanged();
            }
        }

        internal void UpdateContext(Box rect, string surrounding, int cursorBytes, int anchorBytes)
        {
            _cursorRect = rect is { Width: 0, Height: 0 }
                ? new Rect(0, 0, 1, 16)
                : new Rect(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height));
            _surrounding = surrounding;
            _selection = new TextSelection(
                CharIndexOfUtf8Byte(surrounding, anchorBytes),
                CharIndexOfUtf8Byte(surrounding, cursorBytes));
            RaiseCursorRectangleChanged();
            RaiseSurroundingTextChanged();
            RaiseSelectionChanged();
        }

        public override Visual TextViewVisual => _visual!;

        public override bool SupportsPreedit => true;

        public override bool SupportsSurroundingText => true;

        public override string SurroundingText => _surrounding;

        public override Rect CursorRectangle => _cursorRect;

        public override TextSelection Selection
        {
            get => _selection;
            set
            {
            }
        }

        public override void SetPreeditText(string? preeditText) => _owner.PreeditFromHost(preeditText, null);

        public override void SetPreeditText(string? preeditText, int? cursorPos) =>
            _owner.PreeditFromHost(preeditText, cursorPos);

        private static int CharIndexOfUtf8Byte(string text, int byteIndex)
        {
            if (byteIndex <= 0)
            {
                return 0;
            }

            int bytes = 0, chars = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                if (bytes >= byteIndex)
                {
                    break;
                }

                bytes += rune.Utf8SequenceLength;
                chars += rune.Utf16SequenceLength;
            }

            return chars;
        }
    }
}
