using Basin.Seat;
using Basin.Diagnostics;
using BasinGreeter.UI;

namespace BasinGreeter;

internal sealed partial class Greeter
{
    private GreeterKeyboard? _keyboard;
    private Basin.SoftKeyboard.ChromeKeyboardPolicy? _keyboardPolicy;
    private LoginView? _loginView;

    private bool KeyboardAllowed => _config.Settings.OnScreen != "never";

    private void WireKeyboard()
    {
        _keyboard = new GreeterKeyboard(_host.Loop, _seat.Router, _services.Require<Basin.Seat.Seat>().Keyboard, _config.Settings.OnScreenSize, _log);
        _keyboardPolicy = new Basin.SoftKeyboard.ChromeKeyboardPolicy(_ui.TextFields);
        _keyboardPolicy.WantedChanged += (wanted, _) => ShowKeyboard(wanted);
        _keyboard.HideRequested += () => _keyboardPolicy.Hide("hide");
        _model.KeyboardAllowed = KeyboardAllowed;
        _model.KeyboardToggled += () => SetKeyboardWanted(!_model.KeyboardVisible);
        _model.PromptShown += () =>
        {
            if (_config.Settings.OnScreen == "auto")
            {
                _keyboardPolicy.Present();
            }
        };
        _model.Bar.Network.FocusPassphrase += () =>
        {
            if (_config.Settings.OnScreen == "auto")
            {
                _keyboardPolicy.Present();
            }
        };
        AttachKeyboardView();

        var capabilities = _services.Require<Basin.Seat.Seat>().Capabilities;
        if (_config.Settings.OnScreen == "always" ||
            (_config.Settings.OnScreen == "auto" && capabilities.HasFlag(SeatCapability.Touch) && !capabilities.HasFlag(SeatCapability.Keyboard)))
        {
            _keyboardPolicy.Raise();
        }
    }

    private void SetKeyboardWanted(bool wanted)
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

    private void AttachKeyboardView()
    {
        if (_loginView is null || _keyboard is null)
        {
            return;
        }

        var canvas = _loginView.KeyboardCanvas;
        canvas.Engine = _keyboard.Engine;
        canvas.Skin ??= new FlatKeyboardSkin();
        canvas.Clock = () => _keyboard.Now;
        canvas.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Visual.BoundsProperty)
            {
                LayoutKeyboard();
            }
        };
        LayoutKeyboard();
    }

    private void LayoutKeyboard()
    {
        if (_keyboard is null || _loginView is null || _active.Current is null)
        {
            return;
        }

        var box = _layout.BoxOf(_active.Current);
        var width = (int)Math.Round(box.Width / _textScale);
        var height = (int)Math.Round(box.Height / _textScale);
        _model.KeyboardHeight = _keyboard.HeightFor(width, height);
        _keyboard.Layout(width, height);
    }

    private void ShowKeyboard(bool visible)
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
            _keyboard?.Engine.CancelAll();
        }

        BasinReport.Line($"KEYBOARD {(visible ? "on" : "off")}");
        _outputs.ScheduleAll();
    }
}
