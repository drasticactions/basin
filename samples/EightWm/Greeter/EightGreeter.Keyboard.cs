using Avalonia;
using Basin.Diagnostics;
using Basin.Seat;
using Basin.SoftKeyboard;

namespace EightWm;

internal sealed partial class EightGreeter
{
    private SoftKeyboardSession? _keyboard;
    private ChromeKeyboardPolicy? _keyboardPolicy;
    private bool _keyboardVisible;

    private bool KeyboardAllowed => _config.Settings.OnScreen != "never";

    internal static IReadOnlyList<string> GreeterLayouts(string? locale) =>
        locale is null or "" or "C" or "POSIX" || locale.StartsWith("en", StringComparison.OrdinalIgnoreCase) || locale.StartsWith("C.", StringComparison.Ordinal)
            ? KeyboardLayouts.Windows8Ids
            : [SoftKeyboardSessionOptions.XkbStandard, SoftKeyboardSessionOptions.XkbFull];

    private void WireKeyboard()
    {
        var locale = Environment.GetEnvironmentVariable("LC_ALL") is { Length: > 0 } all ? all : Environment.GetEnvironmentVariable("LANG");
        _keyboard = new SoftKeyboardSession(_host.Loop, new SoftKeyboardSessionOptions
        {
            Layouts = GreeterLayouts(locale),
            Size = KeyboardSizes.Parse(_config.Settings.OnScreenSize, KeyboardSize.Medium),
            SpacesToPeriod = false,
            Log = _log,
        });
        var seatKeyboard = _greeter.SeatState.Keyboard;
        _keyboard.Attach(seatKeyboard, null, null, new UISurfaceKeyOutput(_seat.Router, seatKeyboard));
        _keyboardPolicy = new ChromeKeyboardPolicy(_greeter.Ui.TextFields);
        _keyboardPolicy.WantedChanged += (wanted, _) => ShowKeyboard(wanted);
        _keyboard.Engine.HideRequested += () => _keyboardPolicy.Hide("hide");
        _keyboard.LayoutChanged += LayoutKeyboard;
        _keyboard.LayoutSelected += layout => BasinReport.Line($"KBLAYOUT {layout.Id}");
        _keyboard.SizeChanged += _ => LayoutKeyboard();
        _signIn.KeyboardChanged += on => SetKeyboardWanted(on);
        _signIn.FocusPassword += () =>
        {
            if (_curtain.Raised && _config.Settings.OnScreen == "auto")
            {
                _keyboardPolicy.Present();
            }
        };
        AttachKeyboardView();

        var capabilities = _greeter.SeatState.Capabilities;
        if (_config.Settings.OnScreen == "always" ||
            (_config.Settings.OnScreen == "auto" && capabilities.HasFlag(SeatCapability.Touch) && !capabilities.HasFlag(SeatCapability.Keyboard)))
        {
            _keyboardPolicy.Raise();
        }
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

    private void AttachKeyboardView()
    {
        if (_screen is not { } screen || _keyboard is null)
        {
            return;
        }

        var canvas = screen.Keyboard;
        canvas.Engine = _keyboard.Engine;
        canvas.Skin ??= new EightKeyboardSkin(AccentColor);
        canvas.Clock = () => _keyboard.Now;
        screen.KeyboardPanel.IsOpen = _keyboardVisible;
        screen.KeyboardPanel.Settle();
        LayoutKeyboard();
    }

    private void LayoutKeyboard()
    {
        if (_keyboard is null || _screen is not { } screen || _active.Current is not { } output)
        {
            return;
        }

        var box = _layout.BoxOf(output);
        var width = (int)Math.Round(box.Width / _textScale);
        var height = (int)Math.Round(box.Height / _textScale);
        var metrics = KeyboardMetrics.For(_keyboard.Size, width, height, _keyboard.SizeChosen);
        _keyboard.Engine.Layout(width, metrics.KeymapHeight, metrics.Padding);
        screen.Keyboard.Height = metrics.Height;
        _outputs.ScheduleAll();
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

        if (_keyboardVisible == visible)
        {
            return;
        }

        _keyboardVisible = visible;
        _signIn.SetKeyboardQuietly(visible);
        if (_screen is { } screen)
        {
            screen.KeyboardPanel.IsOpen = visible;
        }

        if (!visible)
        {
            _keyboard?.Engine.CancelAll();
        }
        else
        {
            _signIn.RequestFocus();
        }

        LayoutKeyboard();
        BasinReport.Line($"KEYBOARD {(visible ? "on" : "off")}");
    }

    private void StopKeyboard()
    {
        _keyboardPolicy?.Dispose();
        _keyboardPolicy = null;
        _keyboard?.Dispose();
        _keyboard = null;
    }
}
