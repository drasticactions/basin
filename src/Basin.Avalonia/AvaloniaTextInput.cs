using System.Text;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using Basin.Capabilities;

namespace Basin.Avalonia;

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

    internal ToplevelWindow? ActiveWindow { get; private set; }

    public AvaloniaTextInput(Action<Action> postToCompositor)
    {
        ArgumentNullException.ThrowIfNull(postToCompositor);
        _post = postToCompositor;
        Client = new ImeClient(this);
    }

    public bool IsAvailable => true;

    public bool HasKeyboardGrab => false;

    public event Action<PreeditString>? Preedit;

    public event Action<string>? CommitString;

    public event Action<uint, uint>? DeleteSurroundingText
    {
        add
        {
        }

        remove
        {
        }
    }

    public event Action? Done;

    public event Action? AvailabilityChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    internal event Action<ToplevelWindow?>? ActiveWindowChanged;

    internal Func<Surface, int>? IdResolver { get; set; }

    internal Func<int, ToplevelWindow?>? UiWindowResolver { get; set; }

    public void Activate(Surface surface)
    {
        _active = surface;
        var id = IdResolver?.Invoke(surface) ?? 0;
        Dispatcher.UIThread.Post(() =>
        {
            var window = id != 0 ? UiWindowResolver?.Invoke(id) : null;
            ActiveWindow = window;
            Client.SetVisual(window?.View);
            ActiveWindowChanged?.Invoke(window);
            window?.RequeryIme();
        });
    }

    public void Deactivate(Surface surface)
    {
        if (!ReferenceEquals(_active, surface))
        {
            return;
        }

        _active = null;
        Dispatcher.UIThread.Post(() =>
        {
            var window = ActiveWindow;
            ActiveWindow = null;
            Client.SetVisual(null);
            ActiveWindowChanged?.Invoke(null);
            window?.RequeryIme();
        });
    }

    public void SurroundingText(string text, uint cursor, uint anchor)
    {
        _surrounding = text;
        _surroundingCursor = cursor;
        _surroundingAnchor = anchor;
    }

    public void ContentType(uint hint, uint purpose)
    {
    }

    public void CursorRectangle(in Box rect) => _cursorRect = rect;

    public void Commit(uint serial)
    {
        var rect = _cursorRect;
        var surrounding = _surrounding;
        var cursor = (int)_surroundingCursor;
        var anchor = (int)_surroundingAnchor;
        Dispatcher.UIThread.Post(() => Client.UpdateContext(rect, surrounding, cursor, anchor));
    }

    public void ForwardKey(uint timeMs, uint keycode, bool pressed)
    {
    }

    public void ForwardModifiers(uint depressed, uint latched, uint locked, uint group)
    {
    }

    internal bool IsActiveOn(ToplevelWindow window) => ReferenceEquals(ActiveWindow, window);

    internal void CommitFromHost(string text)
    {
        _post(() =>
        {
            if (!_disposed && _active is not null)
            {
                Preedit?.Invoke(new PreeditString(string.Empty, 0, 0));
                CommitString?.Invoke(text);
                Done?.Invoke();
            }
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
