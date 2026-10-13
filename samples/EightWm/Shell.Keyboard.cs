using Avalonia;
using Avalonia.Media;
using Basin;
using Basin.Desktop;
using Basin.Ipc;
using Basin.SoftKeyboard;
using Basin.SoftKeyboard.Avalonia;
using Basin.SoftKeyboard.Ipc;
using Xkb;

namespace EightWm;

internal sealed partial class Shell
{
    internal const int KeyboardCommandTouch = 900;

    private SoftKeyboardSession _kbSession = null!;
    private ShellView? _kbView;
    private bool _kbShown;
    private XkbKeysym _kbChordKey;
    private bool _kbChordSuper = true;
    private bool _kbChordShift;
    private bool _kbChordAlt;
    private bool _kbChordControl;

    internal bool KeyboardShown => _kbShown;

    internal SoftKeyboardSession KeyboardSession => _kbSession;

    internal static string KeyboardStatePath() => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } state
            ? state
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"),
        "eight-wm", "keyboard.json");

    private SoftKeyboardSessionOptions KeyboardOptions()
    {
        var locale = Environment.GetEnvironmentVariable("LC_ALL") is { Length: > 0 } all ? all : Environment.GetEnvironmentVariable("LANG");
        return new SoftKeyboardSessionOptions
        {
            Layouts = _config.KeyboardLayouts.Count > 0 ? _config.KeyboardLayouts : EightGreeter.GreeterLayouts(locale),
            Locale = locale,
            StatePath = KeyboardStatePath(),
            Size = _config.KeyboardSize,
            Policy = SoftKeyboardPolicy.OnTouchFocus,
            TypistTouchId = KeyboardCommandTouch,
            Log = _log,
        };
    }

    private void InstallKeyboard(ProtocolPack pack)
    {
        _kbSession = new SoftKeyboardSession(_host.Loop, KeyboardOptions());
        foreach (var module in pack)
        {
            if (module is InputMethodModule inputMethod)
            {
                inputMethod.Fallback = _kbSession.TextInput;
            }
        }
    }

    private void AttachKeyboard()
    {
        ReadKeyboardChord();
        if (_config.KeyboardEnabled)
        {
            Seat.SetCapability(Basin.Seat.SeatCapability.Keyboard, true);
        }

        _kbSession.SameWindow = (a, b) => OwnerOf(a) is { } owner && ReferenceEquals(OwnerOf(b), owner);
        _kbSession.Attach(Seat.Keyboard, _services.Find<TextInputManager>(), _services.Find<InputMethodRelay>());
        _kbSession.FocusChanged += surface =>
        {
            if (_kbView is not null && (surface is null || OwnerOf(surface) is not { } app || !ReferenceEquals(HomeOf(app), _kbView)))
            {
                _kbSession.ClearIntent();
            }
        };
        _kbSession.WantedChanged += (_, _) => ApplyKeyboard();
        _kbSession.LayoutChanged += () =>
        {
            if (_kbView is { } view)
            {
                PlaceKeyboard(view, animate: false);
            }
        };
        _kbSession.LayoutSelected += layout => _report.Line($"KBLAYOUT {layout.Id}");
        _kbSession.SizeChanged += size =>
        {
            if (_kbView is { } view)
            {
                PlaceKeyboard(view, animate: false);
            }

            PanForKeyboard();
            _report.Line($"KBSIZE {KeyboardSizes.Name(size)}");
            ReportKeyboard();
        };
        _kbSession.Engine.HideRequested += () => HideKeyboard("key");
        AttachPan();
    }

    private void ReadKeyboardChord()
    {
        var parts = _config.KeyboardChord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _kbChordSuper = _kbChordShift = _kbChordAlt = _kbChordControl = false;
        _kbChordKey = default;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "super" or "mod4" or "logo":
                    _kbChordSuper = true;
                    break;
                case "shift":
                    _kbChordShift = true;
                    break;
                case "alt" or "mod1":
                    _kbChordAlt = true;
                    break;
                case "ctrl" or "control":
                    _kbChordControl = true;
                    break;
                default:
                    _kbChordKey = XkbKeysym.FromName(part);
                    break;
            }
        }
    }

    internal bool IsKeyboardChord(XkbKeysym symbol, bool super, bool shift, bool alt, bool control) =>
        _kbChordKey.Value != 0 && symbol == _kbChordKey &&
        super == _kbChordSuper && shift == _kbChordShift && alt == _kbChordAlt && control == _kbChordControl;

    internal void NoteHardwareKey(bool pressed)
    {
        _kbSession.NoteHardwareKey(pressed);
        if (pressed)
        {
            _lockPolicy?.NoteHardwareKey();
        }
    }

    internal void NotePointerInput()
    {
        _kbSession.NotePointer();
        _lockPolicy?.NotePointer();
    }

    internal void NoteClientTouch(Basin.Surface surface) => _kbSession.NoteTouch(surface);

    internal bool IsInputMethodKeyboard(object? tag) => _kbSession.IsInputMethodKeyboard(tag);

    internal void ToggleKeyboard()
    {
        if (_kbShown)
        {
            HideKeyboard("toggle");
        }
        else
        {
            RaiseKeyboard();
        }
    }

    internal void RaiseKeyboard() => _kbSession.Raise();

    internal void HideKeyboard(string why)
    {
        _kbSession.Hide(why);
        _report.Line($"KBHIDE {why}");
    }

    internal bool KeyboardCovers(ShellView view, double localX, double localY) =>
        _kbShown && ReferenceEquals(_kbView, view) && view.KeyboardBox.Width > 0 &&
        localX >= view.KeyboardBox.X && localX < view.KeyboardBox.X + view.KeyboardBox.Width &&
        localY >= view.KeyboardBox.Y && localY < view.KeyboardBox.Y + view.KeyboardBox.Height;

    private ShellView? KeyboardTarget()
    {
        if (!_config.KeyboardEnabled || _locked || !_kbSession.Wanted)
        {
            return null;
        }

        return Focused is { } app ? HomeOf(app) : Views.Count > 0 ? CommandView : null;
    }

    private void ApplyKeyboard()
    {
        var target = KeyboardTarget();
        if (ReferenceEquals(target, _kbView) && (target is not null) == _kbShown)
        {
            return;
        }

        if (_kbView is { } old && !ReferenceEquals(old, target))
        {
            SlideKeyboard(old, show: false);
        }

        _kbView = target;
        _kbShown = target is not null;
        if (target is not null)
        {
            EnsureKeyboardChrome(target);
            PlaceKeyboard(target, animate: false);
            SlideKeyboard(target, show: true);
        }
        else
        {
            _kbSession.NoteHidden();
        }

        PanForKeyboard();
        ReportKeyboard();
    }

    private void EnsureKeyboardChrome(ShellView view)
    {
        if (view.Keyboard is not null)
        {
            return;
        }

        var canvas = new SoftKeyboardCanvas
        {
            Engine = _kbSession.Engine,
            Skin = new EightKeyboardSkin(Color.FromUInt32(AccentNow)),
            Input = SoftKeyboardInput.Pointer,
            Clock = () => _kbSession.Now,
        };
        view.KeyboardCanvas = canvas;
        view.Keyboard = new AvaloniaChrome(view.KeyboardFrame, _ui, _chromeIndex, canvas) { Enabled = true };
    }

    private KeyboardMetrics KeyboardMetricsFor(ShellView view) =>
        KeyboardMetrics.For(_kbSession.Size, view.Box.Width, view.Box.Height, _kbSession.SizeChosen);

    private void PlaceKeyboard(ShellView view, bool animate)
    {
        if (view.Keyboard is not { } chrome)
        {
            return;
        }

        var metrics = KeyboardMetricsFor(view);
        var height = metrics.Height;
        view.KeyboardBox = new Basin.Box(0, view.Box.Height - height, view.Box.Width, height);
        if (ReferenceEquals(view, _kbView))
        {
            _kbSession.Engine.Layout(view.Box.Width, metrics.KeymapHeight, metrics.Padding);
        }

        chrome.Place(view.KeyboardBox, view.Scale);
        view.Scheduler?.ScheduleRepaint();
        if (animate)
        {
            SlideKeyboard(view, show: _kbShown && ReferenceEquals(view, _kbView));
        }
    }

    private void SlideKeyboard(ShellView view, bool show)
    {
        var height = Math.Max(1, view.KeyboardBox.Height);
        var spec = AnimationCatalog.Of(show ? Animation.ShowPanel : Animation.HidePanel) with { Axis = MotionAxis.Y };
        if (show)
        {
            view.KeyboardFrame.Enabled = true;
        }

        Animate(ref view.KeyboardMotion, view.KeyboardFrame, spec, offsetScale: height / Math.Max(1, spec.Offset.From + spec.Offset.To));
        if (!AnimationsOn && !show)
        {
            view.KeyboardFrame.Enabled = false;
        }
    }

    private void AdvanceKeyboard(ShellView view, long nowMillis)
    {
        if (!view.KeyboardMotion.IsRunning)
        {
            return;
        }

        view.KeyboardMotion.Advance(nowMillis);
        view.KeyboardMotion.Apply(view.KeyboardFrame);
        if (!view.KeyboardMotion.IsRunning && !(_kbShown && ReferenceEquals(view, _kbView)))
        {
            view.KeyboardFrame.Enabled = false;
        }
    }

    private void ReportKeyboard()
    {
        var height = _kbView is { } view && _kbShown ? (int)view.KeyboardBox.Height : 0;
        _report.Line($"KEYBOARD {(_kbShown ? "on" : "off")} {height} layout={_kbSession.CurrentLayout.Id} size={KeyboardSizes.Name(_kbSession.Size)}");
    }

    private void RegisterKeyboardCommands(IpcMethodRegistry methods)
    {
        SoftKeyboardSessionIpc.Register(methods, _report, _kbSession, new SoftKeyboardIpcHooks
        {
            Namespace = "eightwm",
            IsShown = () => _kbShown,
            Show = RaiseKeyboard,
            Hide = HideKeyboard,
            Toggle = ToggleKeyboard,
            Report = ReportKeyboard,
        });

        _report.Register(methods, "eightwm/lock-keyboard", "lock-keyboard {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                if (_lockPolicy is not { } policy)
                {
                    ShowLockKeyboard(state != "hide");
                }
                else if (state == "hide")
                {
                    policy.Hide("command");
                }
                else
                {
                    policy.Raise();
                }
            }
        });

        _report.Register(methods, "eightwm/lock-keyboard-type", "lock-keyboard-type {text...}", (ref IpcParams p, IpcReply reply) =>
        {
            var text = p.GetString("text");
            if (!p.Failed && (LockKeyboard is not { } keyboard || !keyboard.Typer.Type(text)))
            {
                reply.Error(IpcErrorCodes.NotFound, "the lock keyboard is not shown or cannot type that");
            }
        });

        _report.Register(methods, "eightwm/lock-locate-password", "lock-locate-password", () =>
        {
            if (_lockView is { } lockView && _lockScreen is { } lockScreen && !lockScreen.KeyboardPanel.IsSliding && lockScreen.SignIn.PasswordBox is { IsEffectivelyVisible: true } box &&
                box.TranslatePoint(new Avalonia.Point(box.Bounds.Width / 2, box.Bounds.Height / 2), lockScreen) is { } center)
            {
                _report.Line($"LOCKAT password {center.X / lockView.Box.Width:0.####} {center.Y / lockView.Box.Height:0.####}");
                return;
            }

            _report.Line("LOCKAT password none");
        });

        _report.Register(methods, "eightwm/keyboard-locate", "keyboard-locate {key}", (ref IpcParams p, IpcReply _) =>
        {
            var key = p.GetString("key");
            if (p.Failed)
            {
                return;
            }

            if (LockKeyboard is { } lockKeyboard && _lockView is { } lockView && _lockScreen is { } lockScreen && !lockScreen.KeyboardPanel.IsSliding &&
                lockKeyboard.Typer.Find(key) is var lockCell and >= 0 &&
                lockScreen.Keyboard.TranslatePoint(default, lockScreen) is { } origin)
            {
                var lockRect = lockKeyboard.Engine.Rects[lockCell];
                _report.Line($"KEYAT {key} {(origin.X + lockRect.CenterX) / lockView.Box.Width:0.####} {(origin.Y + lockKeyboard.Engine.Padding + lockRect.CenterY) / lockView.Box.Height:0.####}");
                return;
            }

            var cell = _kbSession.Typer.Find(key);
            if (!_kbShown || _kbView is not { } view || cell < 0)
            {
                _report.Line($"KEYAT {key} none");
                return;
            }

            var rect = _kbSession.Engine.Rects[cell];
            var x = (view.KeyboardBox.X + rect.CenterX) / view.Box.Width;
            var y = (view.KeyboardBox.Y + _kbSession.Engine.Padding + rect.CenterY) / view.Box.Height;
            _report.Line($"KEYAT {key} {x:0.####} {y:0.####}");
        });
    }

    private void StopKeyboard() => _kbSession?.Dispose();
}
