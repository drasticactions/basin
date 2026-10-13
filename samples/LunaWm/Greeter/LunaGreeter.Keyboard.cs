using Basin.Diagnostics;
using Basin.Seat;
using Basin.SoftKeyboard;
using Basin.SoftKeyboard.Avalonia;

namespace LunaWm;

internal sealed partial class LunaGreeter
{
    private readonly SoftKeyboardTextInput _kbText = new();
    private SoftKeyboardEngine? _kb;
    private KeyboardModel? _kbModel;
    private KeyboardRouter? _kbRouter;
    private SoftKeyboardTyper? _kbTyper;
    private SoftKeyboardTimer? _kbTimer;
    private ChromeKeyboardPolicy? _keyboardPolicy;

    private bool KeyboardAllowed => _config.Settings.OnScreen != "never";

    private void WireKeyboard()
    {
        var locale = Environment.GetEnvironmentVariable("LC_ALL") is { Length: > 0 } all ? all : Environment.GetEnvironmentVariable("LANG");
        var layout = KeyboardLayouts.Load(KeyboardLayouts.DefaultFor(locale), log: _log) ?? KeyboardLayouts.Load("us-qwerty")!;
        _kb = new SoftKeyboardEngine(layout.Grid());
        _kbModel = new KeyboardModel(_kb) { Layout = layout, Size = KeyboardConfig.ParseSize(_config.Settings.OnScreenSize, KeyboardSize.Medium) };
        _kbTyper = new SoftKeyboardTyper(_kb);
        _kbRouter = new KeyboardRouter(_kbText, new UISurfaceKeyOutput(_seat.Router, _services.Require<Seat>().Keyboard)) { SpacesToPeriod = false };
        _kbRouter.Attach(_kb, () => _kbTyper.Clock);
        _kbTimer = new SoftKeyboardTimer(_host.Loop, _kb, () => _kbTyper.Clock);
        _kb.Invalidated += _kbModel.Invalidate;
        _keyboardPolicy = new ChromeKeyboardPolicy(_ui.TextFields);
        _keyboardPolicy.WantedChanged += (wanted, _) => ShowKeyboard(wanted);
        _kb.HideRequested += () => _keyboardPolicy.Hide("hide");
        _model.PromptShown += () =>
        {
            if (_config.Settings.OnScreen == "auto")
            {
                _keyboardPolicy.Present();
            }
        };
        AttachKeyboardView();
        var capabilities = _services.Require<Seat>().Capabilities;
        if (_config.Settings.OnScreen == "always" ||
            (_config.Settings.OnScreen == "auto" && capabilities.HasFlag(SeatCapability.Touch) && !capabilities.HasFlag(SeatCapability.Keyboard)))
        {
            _keyboardPolicy.Raise();
        }
    }

    private void AttachKeyboardView()
    {
        if (_loginView is null || _kbModel is null || _kbTyper is null)
        {
            return;
        }

        var view = _loginView.KeyboardView;
        view.Model = _kbModel;
        view.IsHitTestVisible = true;
        view.Keys.Input = SoftKeyboardInput.Pointer;
        view.Keys.Clock = () => _kbTyper.Clock;
        LayoutKeyboard();
    }

    private void LayoutKeyboard()
    {
        if (_kb is null || _kbModel is null || _active.Current is not { } output)
        {
            return;
        }

        var box = _layout.BoxOf(output);
        var width = (int)Math.Round(box.Width / _textScale);
        var height = (int)Math.Round(box.Height / _textScale);
        var metrics = KeyboardMetrics.For(_kbModel.Size, width, height);
        _kbModel.Width = width;
        _kbModel.Height = metrics.Height;
        _kbModel.Padding = metrics.Padding;
        _kbModel.BarHeight = 0;
        _kb.Layout(width, metrics.KeymapHeight, metrics.Padding);
        _model.KeyboardKeysHeight = metrics.Height;
        _model.KeyboardHeight = metrics.Height + KeyboardModel.PopupRoom;
        _kbModel.Invalidate();
    }

    internal void SetKeyboardWanted(bool wanted)
    {
        if (_keyboardPolicy is not { } policy)
        {
            ShowKeyboard(wanted);
        }
        else if (wanted)
        {
            policy.Raise();
        }
        else
        {
            policy.Hide("toggle");
        }
    }

    private bool OnKey(uint time, uint key, bool pressed)
    {
        if (pressed && key is not (29 or 97 or 42 or 54 or 56 or 100 or 125 or 126 or 58))
        {
            _keyboardPolicy?.NoteHardwareKey();
        }

        return false;
    }

    internal void ShowKeyboard(bool visible)
    {
        if (visible && !KeyboardAllowed)
        {
            return;
        }

        if (_config.Settings.OnScreen == "always")
        {
            visible = true;
        }

        if (_model.KeyboardVisible == visible)
        {
            return;
        }

        LayoutKeyboard();
        _model.KeyboardVisible = visible;
        if (!visible)
        {
            _kb?.CancelAll();
        }
        else
        {
            _loginView?.RequestFocus();
        }

        BasinReport.Line($"KEYBOARD {(visible ? "on" : "off")}");
        _outputs.ScheduleAll();
    }

    private void StopKeyboard()
    {
        _keyboardPolicy?.Dispose();
        _keyboardPolicy = null;
        _kbTimer?.Dispose();
        _kbTimer = null;
    }
}
