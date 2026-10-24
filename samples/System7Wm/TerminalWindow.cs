using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Basin;
using Basin.Pty;
using SvcSystems.UI.Terminal;
using System7.Avalonia.Rendering;
using XTerm.Events;

namespace System7Wm;

internal sealed class TerminalWindow : IDisposable
{
    public const int BarSize = 15;

    public const int Border = 2;

    private readonly ICompositorEventLoop _loop;
    private readonly PtyStartInfo _start;
    private readonly byte[] _carry = new byte[4];
    private ScrollBar _inner = null!;
    private readonly ScrollBar _vertical;
    private readonly ScrollBar _horizontal;
    private int _carried;
    private bool _mirroring;
    private bool _disposed;

    public TerminalWindow(ICompositorEventLoop loop, PtyStartInfo start, string fallbackTitle, int fontSize, int scrollback, System7ColorDepth depth)
    {
        _loop = loop;
        _start = start;
        FallbackTitle = fallbackTitle;
        FontSize = TerminalFonts.IsSize(fontSize) ? fontSize : 9;
        Model = new TerminalControlModel(new TerminalOptions
        {
            Cols = start.Columns,
            Rows = start.Rows,
            Scrollback = Math.Max(scrollback, 0),
            TermName = "xterm-256color",
        });
        var engine = Model.Terminal.Engine;
        engine.Options.ClipboardWriteEnabled = true;
        engine.Options.ClipboardReadEnabled = false;
        engine.Options.WindowOptions = new XTerm.Options.WindowOptions
        {
            GetWinPosition = false,
            GetWinSizePixels = false,
            GetWinSizeChars = false,
            GetScreenSizePixels = false,
            GetCellSizePixels = false,
            GetIconTitle = false,
            GetWinTitle = false,
            GetWinState = false,
            SetWinPosition = false,
            SetWinSizePixels = false,
            SetWinSizeChars = false,
            RaiseWin = false,
            LowerWin = false,
            RefreshWin = false,
            RestoreWin = false,
            MaximizeWin = false,
            MinimizeWin = false,
            FullscreenWin = false,
            RequestAttention = false,
        };
        engine.DataReceived += OnReply;
        engine.BellRang += OnBell;
        engine.ClipboardWriteRequested += OnClipboardWrite;
        engine.DirectoryChanged += OnDirectory;
        Model.Terminal.TitleChanged += OnTitle;
        Model.SizeChanged += OnSizeChanged;
        Model.UserInput += OnUserInput;

        Control = BuildControl(depth);

        _vertical = new ScrollBar
        {
            Name = "TerminalVerticalScrollBar",
            Orientation = Orientation.Vertical,
            Visibility = ScrollBarVisibility.Visible,
            AllowAutoHide = false,
            Margin = new Thickness(0, -1, -1, -1),
        };
        _horizontal = new ScrollBar
        {
            Name = "TerminalHorizontalScrollBar",
            Orientation = Orientation.Horizontal,
            Visibility = ScrollBarVisibility.Visible,
            AllowAutoHide = false,
            Minimum = 0,
            Maximum = 0,
            IsEnabled = false,
            Margin = new Thickness(-1, 0, -1, -1),
        };
        if (Application.Current?.TryFindResource("System7ScrollBarTheme", out var theme) == true && theme is ControlTheme bars)
        {
            _vertical.Theme = bars;
            _horizontal.Theme = bars;
        }

        _vertical.ValueChanged += OnVerticalChanged;
        Mirror();

        Avalonia.Controls.Grid.SetColumn(_vertical, 1);
        Avalonia.Controls.Grid.SetRow(_horizontal, 1);
        View = new Grid
        {
            Background = new Avalonia.Media.SolidColorBrush(TerminalPalette.Paper),
            ColumnDefinitions = [new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(BarSize, GridUnitType.Pixel)],
            RowDefinitions = [new RowDefinition(1, GridUnitType.Star), new RowDefinition(BarSize, GridUnitType.Pixel)],
            Children = { Control, _vertical, _horizontal },
        };
        View.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            {
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public Grid View { get; }

    public TerminalControl Control { get; private set; }

    public TerminalControlModel Model { get; }

    public PtySession? Session { get; private set; }

    public int FontSize { get; private set; }

    public string FallbackTitle { get; }

    public string Title => Model.Title is { Length: > 0 } title ? title : FallbackTitle;

    public string? Directory { get; private set; }

    public string? Failure { get; private set; }

    public PtyExit? Exit { get; private set; }

    public (int Columns, int Rows) Cells => (Model.Terminal.Cols, Model.Terminal.Rows);

    public event Action? Started;

    public event Action<string>? TitleChanged;

    public event Action<PtyExit>? Exited;

    public event Action? Bell;

    public event Action<string>? FailedToStart;

    public void Start()
    {
        if (Session is not null || Failure is not null || _disposed)
        {
            return;
        }

        var (columns, rows) = Cells;
        var start = _start with { Columns = columns, Rows = rows };
        Session = PtySession.TryStart(_loop, start, out var failure);
        if (Session is null)
        {
            Failure = failure ?? "no reason given";
            Feed(Encoding.UTF8.GetBytes($"[The shell couldn't start: {Failure}]\r\n"));
            FailedToStart?.Invoke(Failure);
            return;
        }

        Session.Output += Receive;
        Session.Exited += OnExited;
        Started?.Invoke();
    }

    public void Send(ReadOnlySpan<byte> data) => Session?.Write(data);

    public void SetFontSize(int size)
    {
        if (!TerminalFonts.IsSize(size) || size == FontSize)
        {
            return;
        }

        FontSize = size;
        Control.FontFamily = TerminalFonts.Family(size);
        Control.FontSize = size;
    }

    public void SetDepth(System7ColorDepth depth)
    {
        var focused = Control.IsFocused;
        var old = Control;
        old.Model = null;
        _inner.PropertyChanged -= OnInnerChanged;
        View.Children.Remove(old);
        Control = BuildControl(depth);
        View.Children.Insert(0, Control);
        Mirror();
        if (focused)
        {
            Control.Focus();
        }
    }

    private TerminalControl BuildControl(System7ColorDepth depth)
    {
        var control = new TerminalControl
        {
            Margin = new Thickness(Border, Border, Border, 0),
            FontFamily = TerminalFonts.Family(FontSize),
            FontSize = FontSize,
            CaretBrush = new ImmutableSolidColorBrush(TerminalPalette.Ink),
            SelectionBrush = Application.Current?.TryFindResource("System7SelectionBackgroundBrush", out var brush) == true
                ? brush as Avalonia.Media.IBrush
                : null,
        };
        TerminalPalette.Apply(control.Resources, depth);
        control.Model = Model;
        _inner = control.Children.OfType<ScrollBar>().First();
        _inner.MaxWidth = 0;
        _inner.Opacity = 0;
        _inner.IsHitTestVisible = false;
        _inner.PropertyChanged += OnInnerChanged;
        control.AttachedToVisualTree += (_, _) => control.Focus();
        return control;
    }

    public void ClearScrollback()
    {
        var engine = Model.Terminal.Engine;
        if (engine.IsAlternateBufferActive)
        {
            return;
        }

        engine.Write("\u001b[3J"u8);
        Model.UpdateDisplay();
    }

    public void ShowExit(PtyExit exit)
    {
        var line = exit.Signal is { } signal
            ? $"[Process killed by {TerminalSignals.Name(signal)}]"
            : $"[Process exited: {exit.Status}]";
        Feed(Encoding.UTF8.GetBytes($"\r\n{line}\r\n"));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Session is { } session)
        {
            session.Output -= Receive;
            session.Exited -= OnExited;
            session.Dispose();
        }

        Session = null;
        var engine = Model.Terminal.Engine;
        engine.DataReceived -= OnReply;
        engine.BellRang -= OnBell;
        engine.ClipboardWriteRequested -= OnClipboardWrite;
        engine.DirectoryChanged -= OnDirectory;
        Model.Terminal.TitleChanged -= OnTitle;
        Model.SizeChanged -= OnSizeChanged;
        Model.UserInput -= OnUserInput;
        _inner.PropertyChanged -= OnInnerChanged;
        Model.Dispose();
    }

    internal void Receive(ReadOnlySpan<byte> data)
    {
        if (_carried == 0)
        {
            Feed(data);
            return;
        }

        var joined = new byte[_carried + data.Length];
        _carry.AsSpan(0, _carried).CopyTo(joined);
        data.CopyTo(joined.AsSpan(_carried));
        _carried = 0;
        Feed(joined);
    }

    private void Feed(ReadOnlySpan<byte> data)
    {
        var complete = Utf8.CompleteLength(data);
        if (complete < data.Length)
        {
            data[complete..].CopyTo(_carry);
            _carried = data.Length - complete;
            data = data[..complete];
        }

        if (data.IsEmpty || _disposed)
        {
            return;
        }

        Model.SearchService.Invalidate();
        var atBottom = Model.Terminal.Buffer.IsAtBottom;
        Model.Terminal.Engine.Write(data);
        Model.UpdateDisplay();
        if (atBottom)
        {
            Model.EnsureCaretIsVisible();
        }
    }

    private void OnExited(PtyExit exit)
    {
        Exit = exit;
        Exited?.Invoke(exit);
    }

    private void OnSizeChanged(object? sender, TerminalSizeChangedEventArgs e)
    {
        if (Session is { } session)
        {
            session.Resize(e.Cols, e.Rows, (int)e.Width, (int)e.Height);
            return;
        }

        Start();
    }

    private void OnUserInput(object? sender, TerminalUserInputEventArgs e) => Session?.Write(e.Data.Span);

    private void OnReply(object? sender, TerminalEvents.DataEventArgs e)
    {
        if (e.Data is { Length: > 0 } reply)
        {
            Session?.Write(Encoding.UTF8.GetBytes(reply));
        }
    }

    private void OnBell(object? sender, EventArgs e) => Bell?.Invoke();

    private void OnClipboardWrite(object? sender, TerminalEvents.ClipboardWriteEventArgs e)
    {
        if (e.Text is { } text && TopLevel.GetTopLevel(Control)?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(text);
        }
    }

    private void OnDirectory(object? sender, TerminalEvents.DirectoryChangeEventArgs e) => Directory = e.Directory;

    private void OnTitle(object? sender, TitleChangedEventArgs e) => TitleChanged?.Invoke(Title);

    private void OnInnerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == RangeBase.ValueProperty || e.Property == RangeBase.MaximumProperty ||
            e.Property == ScrollBar.ViewportSizeProperty || e.Property == RangeBase.LargeChangeProperty ||
            e.Property == InputElement.IsEnabledProperty)
        {
            Mirror();
        }
    }

    private void Mirror()
    {
        _mirroring = true;
        try
        {
            _vertical.Minimum = 0;
            _vertical.Maximum = _inner.Maximum;
            _vertical.ViewportSize = _inner.ViewportSize;
            _vertical.SmallChange = 1;
            _vertical.LargeChange = _inner.LargeChange;
            _vertical.Value = _inner.Value;
            _vertical.IsEnabled = _inner.IsEnabled && _inner.Maximum > 0;
        }
        finally
        {
            _mirroring = false;
        }
    }

    private void OnVerticalChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_mirroring)
        {
            _inner.Value = e.NewValue;
        }
    }
}
