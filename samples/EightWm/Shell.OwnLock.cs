using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Basin;
using Basin.Scene;
using Basin.Session;

namespace EightWm;

internal sealed partial class Shell
{
    internal const int LockoutAttempts = 5;
    internal const int LockoutMillis = 30_000;

    private readonly EightLockState _lockState = new();
    private readonly Dictionary<ShellView, AvaloniaChrome> _lockScreens = [];
    private readonly SignInModel _lockSignIn = new() { OptionsAvailable = false, PowerAvailable = false };
    private readonly CurtainModel _lockCurtain = new();
    private readonly NetworkModel _lockNetwork = new() { CanJoin = false };
    private readonly List<CurtainPair> _lockPairs = [];
    private SceneTree _ownLockLayer = null!;
    private GreeterScreen? _lockScreen;
    private ShellView? _lockView;
    private Bitmap? _lockPicture;
    private bool _ownLocked;
    private bool _externalLocked;
    private PamCheck? _pamCheck;
    private int _failures;
    private IEventSource? _lockout;
    private IEventSource? _idleTimer;
    private IEventSource? _lockClock;
    private Basin.SoftKeyboard.SoftKeyboardSession? _lockKeyboard;
    private bool _lockKeyboardShown;
    private Basin.SoftKeyboard.ChromeKeyboardPolicy? _lockPolicy;

    internal bool OwnLockShown => _ownLocked && !_externalLocked;

    internal bool OwnLocked => _ownLocked;

    private void CreateOwnLock()
    {
        _ownLockLayer = new SceneTree(_scene.Root) { Enabled = false };
        _lockSignIn.Submitted += SubmitUnlock;
        _lockSignIn.RevealAllowed = true;
        _lockCurtain.RaiseRequested += how =>
        {
            if (_lockCurtain.Raised)
            {
                return;
            }

            _lockCurtain.Raised = true;
            foreach (var pair in _lockPairs)
            {
                pair.Over.Raised = true;
            }

            _report.Line($"CURTAIN up {how}");
            _lockSignIn.RequestFocus();
            if (how == "swipe")
            {
                _lockPolicy?.NoteTouch();
            }

            _lockPolicy?.Present();

            _outputs.ScheduleAll();
        };
    }

    internal void Lock()
    {
        if (_ownLocked)
        {
            return;
        }

        _ownLocked = true;
        _failures = 0;
        _lockPolicy?.Hide("lock");
        var name = Environment.UserName;
        var display = Environment.GetEnvironmentVariable("EIGHTWM_LOCK_NAME") is { Length: > 0 } shown ? shown : DisplayNameOf(name);
        _lockSignIn.Users.Clear();
        var tile = new UserTileModel(name, display, null);
        _lockSignIn.Users.Add(tile);
        _lockSignIn.ShowUsers(tile);
        _lockSignIn.BeginConversation(tile);
        _lockSignIn.ShowPrompt("Password", secret: true);
        _lockSignIn.Message = "";
        _lockCurtain.Raised = false;
        BuildLockScreens();
        ApplyLock();
        _report.Line("LOCKED");
    }

    internal void Unlock()
    {
        if (!_ownLocked)
        {
            return;
        }

        _ownLocked = false;
        _lockPolicy?.Hide("unlock");
        _pamCheck?.Dispose();
        _pamCheck = null;
        _lockout?.Remove();
        _lockout = null;
        _lockKeyboardShown = false;
        ApplyLock();
        DisposeLockScreens();
        _report.Line("UNLOCKED");
    }

    private void SetExternalLocked(bool locked)
    {
        _externalLocked = locked;
        ApplyLock();
    }

    private void ApplyLock()
    {
        var locked = _ownLocked || _externalLocked;
        if (locked != _locked)
        {
            SetLocked(locked);
        }

        _lockState.Set(locked);
        _ownLockLayer.Enabled = OwnLockShown;
        _ownLockLayer.RaiseToTop();
        _lockLayer.RaiseToTop();
        if (OwnLockShown && _lockScreens.TryGetValue(_lockView ?? PrimaryView, out var chrome) && chrome.Surface is { } surface)
        {
            _router.SetKeyboardFocus(surface);
            if (_lockScreen is { } screen)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_lockCurtain.Raised)
                    {
                        screen.SignIn.PasswordBox.Focus();
                    }
                    else
                    {
                        screen.Curtain.Focus();
                    }
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
        }

        _outputs.ScheduleAll();
    }

    private void BuildLockScreens()
    {
        DisposeLockScreens();
        _lockPicture = LoadLockPicture();
        var accent = Color.FromUInt32(AccentNow);
        if (Avalonia.Application.Current is { } app)
        {
            SignInTheme.Install(app, accent);
        }

        _lockView = Views.Count > 0 ? CommandView : PrimaryView;
        foreach (var view in Views)
        {
            AddLockScreen(view);
        }

        _lockClock ??= _host.Loop.AddTimer(UpdateLockClock);
        UpdateLockClock();
    }

    private void AddLockScreen(ShellView view)
    {
        if (_lockScreens.ContainsKey(view))
        {
            return;
        }

        Control content;
        if (ReferenceEquals(view, _lockView))
        {
            Dress(_lockCurtain, dimmed: false);
            _lockScreen = new GreeterScreen(_lockSignIn, _lockCurtain, _lockNetwork);
            AttachLockKeyboard(view, _lockScreen);
            content = _lockScreen;
        }
        else
        {
            var over = new CurtainModel { Raised = _lockCurtain.Raised };
            var under = new CurtainModel();
            Dress(over, dimmed: false);
            Dress(under, dimmed: true);
            var pair = new CurtainPair(under, over);
            _lockPairs.Add(pair);
            content = pair;
        }

        var chrome = new AvaloniaChrome(_ownLockLayer, _ui, _chromeIndex, content) { Enabled = true };
        chrome.Place(view.Box, view.Scale);
        _lockScreens[view] = chrome;
    }

    private void AttachLockKeyboard(ShellView view, GreeterScreen screen)
    {
        if (_lockKeyboard is null)
        {
            var locale = Environment.GetEnvironmentVariable("LC_ALL") is { Length: > 0 } all ? all : Environment.GetEnvironmentVariable("LANG");
            _lockKeyboard = new Basin.SoftKeyboard.SoftKeyboardSession(_host.Loop, new Basin.SoftKeyboard.SoftKeyboardSessionOptions
            {
                Layouts = EightGreeter.GreeterLayouts(locale),
                SpacesToPeriod = false,
                TypistTouchId = KeyboardCommandTouch + 1,
                Log = _log,
            });
            _lockKeyboard.Attach(Seat.Keyboard, null, null, new Basin.SoftKeyboard.UISurfaceKeyOutput(_router, Seat.Keyboard));
            _lockPolicy = new Basin.SoftKeyboard.ChromeKeyboardPolicy(_ui.TextFields);
            _lockPolicy.WantedChanged += (wanted, _) => ShowLockKeyboard(wanted && OwnLockShown && _lockCurtain.Raised);
            _lockKeyboard.Engine.HideRequested += () => _lockPolicy.Hide("hide");
        }

        var canvas = screen.Keyboard;
        canvas.Engine = _lockKeyboard.Engine;
        canvas.Skin = new EightKeyboardSkin(Color.FromUInt32(AccentNow));
        canvas.Clock = () => _lockKeyboard.Now;
        var metrics = Basin.SoftKeyboard.KeyboardMetrics.For(_lockKeyboard.Size, view.Box.Width, view.Box.Height);
        _lockKeyboard.Engine.Layout(view.Box.Width, metrics.KeymapHeight, metrics.Padding);
        canvas.Height = metrics.Height;
        screen.KeyboardPanel.IsOpen = _lockKeyboardShown;
        screen.KeyboardPanel.Settle();
    }

    internal void ShowLockKeyboard(bool shown)
    {
        if (_lockScreen is not { } screen || _lockKeyboard is null || _lockKeyboardShown == shown)
        {
            return;
        }

        _lockKeyboardShown = shown;
        screen.KeyboardPanel.IsOpen = shown;
        if (!shown)
        {
            _lockKeyboard.Engine.CancelAll();
        }

        _report.Line($"LOCKKEYBOARD {(shown ? "on" : "off")}");
        _outputs.ScheduleAll();
    }

    internal Basin.SoftKeyboard.SoftKeyboardSession? LockKeyboard => _lockKeyboardShown ? _lockKeyboard : null;

    private void Dress(CurtainModel curtain, bool dimmed)
    {
        var accent = Color.FromUInt32(AccentNow);
        curtain.Picture = _lockPicture;
        curtain.Dimmed = dimmed;
        curtain.Fill = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(accent, 0),
                new GradientStop(Color.FromRgb((byte)(accent.R / 2), (byte)(accent.G / 2), (byte)(accent.B / 2)), 1),
            },
        };
    }

    private void PlaceLockScreens()
    {
        foreach (var (view, chrome) in _lockScreens)
        {
            chrome.Place(view.Box, view.Scale);
        }
    }

    private void DisposeLockScreens()
    {
        foreach (var chrome in _lockScreens.Values)
        {
            chrome.Dispose();
        }

        _lockScreens.Clear();
        _lockPairs.Clear();
        _lockClock?.Remove();
        _lockClock = null;
        _lockScreen = null;
        _lockPicture?.Dispose();
        _lockPicture = null;
    }

    private void UpdateLockClock()
    {
        if (!_ownLocked)
        {
            return;
        }

        var now = DateTime.Now;
        var clock = _lockClockOverride ?? now.ToString("h:mm", CultureInfo.InvariantCulture);
        var date = _lockClockOverride is not null ? "Monday, January 2" : now.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);
        _lockCurtain.Clock = clock;
        _lockCurtain.Date = date;
        foreach (var pair in _lockPairs)
        {
            pair.Under.Clock = pair.Over.Clock = clock;
            pair.Under.Date = pair.Over.Date = date;
        }

        _lockClock?.UpdateTimer(Math.Max(50, ((60 - now.Second) * 1000) - now.Millisecond));
    }

    internal void SetLockClock(string? text)
    {
        _lockClockOverride = text is { Length: > 0 } ? text : null;
        UpdateLockClock();
    }

    internal void UnlockType(string text)
    {
        if (!OwnLockShown || _lockScreen is not { } screen)
        {
            _report.Line("UNLOCK not-locked");
            return;
        }

        if (!_lockCurtain.Raised)
        {
            _lockCurtain.RequestRaise("command");
        }

        _lockSignIn.Password = text;
        screen.SignIn.PasswordBox.Focus();
    }

    internal void UnlockSubmit()
    {
        if (OwnLockShown)
        {
            _lockSignIn.Submit.Execute(null);
        }
    }

    private string? _lockClockOverride;

    private Bitmap? LoadLockPicture()
    {
        if (string.IsNullOrEmpty(_config.LockPicture))
        {
            return null;
        }

        try
        {
            return new Bitmap(_config.LockPicture);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _log.Warn($"cannot load {_config.LockPicture}: {error.Message}");
            return null;
        }
    }

    private static string DisplayNameOf(string user)
    {
        try
        {
            foreach (var line in File.ReadLines("/etc/passwd"))
            {
                var fields = line.Split(':');
                if (fields.Length > 4 && fields[0] == user && fields[4].Split(',')[0] is { Length: > 0 } gecos)
                {
                    return gecos;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }

        return user;
    }

    private void SubmitUnlock(string text)
    {
        if (!_ownLocked)
        {
            return;
        }

        if (_lockout is not null || _pamCheck is not null)
        {
            _lockSignIn.Busy = _lockout is not null || _pamCheck is not null;
            return;
        }

        if (text.Length == 0)
        {
            _lockSignIn.Busy = false;
            _lockSignIn.Message = "Enter your password";
            return;
        }

        var password = Encoding.UTF8.GetBytes(text);
        _lockSignIn.Message = "";
        IReadOnlyList<string> argv = _options.PamHelperCommand is { Length: > 0 } stub
            ? ["sh", "-c", stub]
            : [Environment.ProcessPath ?? "eight-wm", "--pam-helper", _config.PamService, Environment.UserName];
        _pamCheck = PamCheck.Start(_host.Loop, argv, password, UnlockResult, out var error);
        Array.Clear(password);
        if (_pamCheck is null)
        {
            _lockSignIn.Busy = false;
            _lockSignIn.ShowError("The password can't be checked.");
            _log.Warn($"unlock check did not start: {error}");
            _report.Line("UNLOCK error");
            return;
        }

        _report.Line("UNLOCK checking");
    }

    private void UnlockResult(bool success)
    {
        _pamCheck = null;
        if (success)
        {
            _lockSignIn.Busy = false;
            _failures = 0;
            Unlock();
            return;
        }

        _failures++;
        _report.Line($"UNLOCK failed {_failures}");
        if (_failures < LockoutAttempts)
        {
            _lockSignIn.ShowError("The password is incorrect. Try again.");
            return;
        }

        _lockSignIn.ShowError("Too many attempts. Try again in 30 seconds.");
        _lockSignIn.Busy = true;
        _report.Line("UNLOCK lockout");
        _lockout = _host.Loop.AddTimer(() =>
        {
            _lockout?.Remove();
            _lockout = null;
            _failures = 0;
            _lockSignIn.Busy = false;
            _lockSignIn.Error = "";
            _report.Line("UNLOCK ready");
        });
        _lockout.UpdateTimer(LockoutMillis);
    }

    private void StartIdleLock()
    {
        _idleTimer?.Remove();
        _idleTimer = null;
        if (_config.IdleLockSeconds <= 0 || _services.Find<Basin.Capabilities.IIdleSource>() is not Basin.Seat.SeatIdleSource idle)
        {
            return;
        }

        _idleTimer = _host.Loop.AddTimer(() =>
        {
            var limit = _config.IdleLockSeconds * 1000L;
            var waited = idle.IdleMillis;
            if (!_ownLocked && limit > 0 && waited >= limit)
            {
                Lock();
            }

            _idleTimer?.UpdateTimer((int)Math.Max(1000, limit - (_ownLocked ? 0 : waited)));
        });
        _idleTimer.UpdateTimer(_config.IdleLockSeconds * 1000);
    }

    private void StopOwnLock()
    {
        _idleTimer?.Remove();
        _idleTimer = null;
        _lockout?.Remove();
        _lockout = null;
        _pamCheck?.Dispose();
        _pamCheck = null;
        DisposeLockScreens();
        _lockPolicy?.Dispose();
        _lockPolicy = null;
        _lockKeyboard?.Dispose();
        _lockKeyboard = null;
    }
}
