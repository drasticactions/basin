using System.Text.Json;
using Basin;
using Basin.Audio.PipeWire;
using Basin.Desktop;
using Basin.Ipc;
using Basin.Scene;
using Basin.SoftKeyboard;
using Basin.SoftKeyboard.Ipc;

namespace LunaWm;

internal sealed partial class LunaShell
{
    private const string KeyboardEvent = "lunawm/keyboard";
    private const double KeyboardEdgeTravel = 60;

    private readonly KeyboardTouch[] _kbTouches = new KeyboardTouch[SoftKeyboardEngine.MaxTouches];
    private SoftKeyboardSession _kbSession = null!;
    private SoftKeyboardEngine _kb = null!;
    private KeyboardModel _kbModel = null!;
    private TextInputManager? _textInputs;
    private InputMethodRelay? _imRelay;
    private Action _kbIdle = null!;
    private bool _kbIdleQueued;
    private LunaView? _kbView;
    private bool _kbPending;
    private bool _kbProbing;
    private ShiftMode _kbShiftReported;
    private SoundClip? _kbClick;
    private SoundClip? _kbSpace;
    private SoundClip? _kbDelete;
    private SoundClip? _kbReturn;
    private SoundClip? _kbTick;

    private struct KeyboardTouch
    {
        public int Id;
        public bool Active;
        public bool Edge;
        public bool Claimed;
        public bool Bar;
        public double LastX;
        public double X0;
        public double Y0;
        public uint Began;
        public Basin.Seat.VelocityTracker Tracker;
    }

    internal long KeyboardNow => _kbSession.Now;

    internal bool KeyboardShown => _kbView is { Keyboard.Shown: true };

    private void InstallKeyboard(ProtocolPack pack)
    {
        _kbSession = new SoftKeyboardSession(_host.Loop, KeyboardOptions());
        _kb = _kbSession.Engine;
        foreach (var module in pack)
        {
            if (module is InputMethodModule inputMethod)
            {
                inputMethod.Fallback = _kbSession.TextInput;
            }
        }
    }

    private SoftKeyboardSessionOptions KeyboardOptions() => new()
    {
        Layouts = _config.Keyboard.Layouts,
        Locale = Environment.GetEnvironmentVariable("LC_ALL") is { Length: > 0 } all ? all : Environment.GetEnvironmentVariable("LANG"),
        UserLayoutDirectory = KeyboardConfig.UserLayoutDirectory(),
        StatePath = KeyboardStatePath(),
        Timing = _config.Keyboard.Timing,
        Diamonds = _config.Keyboard.Diamonds,
        SpacesToPeriod = _config.Keyboard.SpacesToPeriod,
        Size = _config.Keyboard.Size,
        TypistTouchId = KeyboardCommandTouch,
        Log = _log,
    };

    private void AttachKeyboard()
    {
        _textInputs = _services.Find<TextInputManager>();
        _imRelay = _services.Find<InputMethodRelay>();
        if (_config.Keyboard.Enabled)
        {
            Seat.SetCapability(Basin.Seat.SeatCapability.Keyboard, true);
        }

        _kbSession.SameWindow = (a, b) => OwnerOf(a) is { } owner && ReferenceEquals(OwnerOf(b), owner);
        _kbSession.Attach(Seat.Keyboard, _textInputs, _imRelay);
        _kbModel = new KeyboardModel(_kb) { Layout = _kbSession.CurrentLayout, Size = _kbSession.Size };
        _kbSession.Router.Sent += OnKeySent;
        _kbSession.FocusChanged += OnKeyboardFocus;
        _kbSession.TextInputChanged += SyncFcitx5Field;
        _kbSession.InputMethodChanged += ReportKeyboard;
        _kbSession.WantedChanged += (_, _) => ApplyKeyboard();
        _kbSession.LayoutChanged += OnKeyboardLayoutChanged;
        _kbSession.LayoutSelected += layout =>
        {
            UpdateTraceEnabled();
            _report.Line($"LAYOUT {layout.Id}");
        };
        _kbSession.SizeChanged += OnKeyboardSizeChanged;
        StartFcitx5();
        AttachTrace();
        _kbIdle = () =>
        {
            _kbIdleQueued = false;
            FlushKeyboard();
        };
        _kb.Invalidated += _kbModel.Invalidate;
        _kb.State.Changed += OnKeyboardStateChanged;
        _kb.Clicked += PlayKeySound;
        _kb.LongPressOpened += (_, _) => PlayClip(_kbTick);
        _kb.HideRequested += () => HideKeyboard("key");
        foreach (var view in _views)
        {
            AttachKeyboardSurface(view);
        }
    }

    private void StopKeyboard()
    {
        StopFcitx5();
        StopTrace();
        _kbSession.Dispose();
    }

    private void AttachKeyboardSurface(LunaView view)
    {
        if (_kbModel is null || view.Keyboard is not null)
        {
            return;
        }

        view.Keyboard = new KeyboardSurface(view.InputMethod, _ui, _chromeIndex, _kbModel);
        view.Keyboard.Slide.Jump(0);
    }

    private void OnKeyboardLayoutChanged()
    {
        _kbModel.Layout = _kbSession.CurrentLayout;
        if (_kbView is { } view)
        {
            PlaceKeyboard(view, immediate: true);
        }

        _kbModel.Invalidate();
    }

    private void ChooseKeyboardSize(KeyboardSize size) => _kbSession.ChooseSize(size);

    private void OnKeyboardSizeChanged(KeyboardSize size)
    {
        _kbModel.Size = size;
        if (_kbView is { } view)
        {
            PlaceKeyboard(view, immediate: true);
        }

        _report.Line($"KBSIZE {KeyboardConfig.SizeName(size)}");
        ReportKeyboard();
    }

    private void OnKeyboardFocus(Surface? surface)
    {
        if (_kbView is { } view && (surface is null || OwnerOf(surface) is not { } card || !ReferenceEquals((card.Parent ?? card).Home, view) ||
            !ReferenceEquals(card.Parent ?? card, view.Deck.ActiveCard)))
        {
            _kbSession.ClearIntent();
        }
    }

    internal void NoteInput(LastInput input) => _kbSession.NoteInput(input);

    internal bool IsInputMethodKeyboard(object? tag) => _kbSession.IsInputMethodKeyboard(tag);

    internal void NoteHardwareKey(bool pressed) => _kbSession.NoteHardwareKey(pressed);

    internal void NoteClientTouch(Surface surface) => _kbSession.NoteTouch(surface);

    internal void ToggleKeyboard()
    {
        if (KeyboardShown)
        {
            HideKeyboard("toggle");
            return;
        }

        RaiseKeyboard();
    }

    internal void RaiseKeyboard() => _kbSession.Raise();

    internal void HideKeyboard(string why)
    {
        _kbSession.Hide(why);
        _report.Line($"KBHIDE {why}");
    }

    private LunaView? KeyboardTarget()
    {
        if (!_config.Keyboard.Enabled || _locked || _sessionLocked || !_kbSession.Wanted)
        {
            return null;
        }

        if (Focused is not { } card || (card.Parent ?? card).Home is not { } view || view.InCardView)
        {
            return null;
        }

        if (LauncherOpen(view) || JustTypeOpen(view) || MenuOpen(view) || DashboardOpen(view))
        {
            return null;
        }

        return view;
    }

    private void ApplyKeyboard()
    {
        if (_kb is null)
        {
            return;
        }

        var target = KeyboardTarget();
        if (ReferenceEquals(target, _kbView))
        {
            return;
        }

        if (_seat.LiveContacts > 0)
        {
            _kbPending = true;
            return;
        }

        _kbPending = false;
        if (_kbView is { } old)
        {
            CloseKeyboardOn(old, animate: target is null);
        }

        _kbView = target;
        if (target is not null)
        {
            OpenKeyboardOn(target);
        }
        else
        {
            _kbSession.NoteHidden();
        }

        ReportKeyboard();
    }

    internal void KeyboardTouchesEnded()
    {
        if (_kbPending)
        {
            ApplyKeyboard();
        }
    }

    private KeyboardMetrics KeyboardMetricsFor(LunaView view) =>
        KeyboardMetrics.For(_kbSession.Size, view.Box.Width, view.Box.Height, _kbSession.SizeChosen);

    private int KeyboardInset(LunaView view) => KeyboardMetricsFor(view).Height + (int)_kbModel.BarHeight;

    private void PlaceKeyboard(LunaView view, bool immediate)
    {
        if (view.Keyboard is not { } surface)
        {
            return;
        }

        var metrics = KeyboardMetricsFor(view);
        var inset = metrics.Height + (int)_kbModel.BarHeight;
        surface.Height = inset;
        _kbModel.Width = view.Box.Width;
        _kbModel.Height = inset;
        _kbModel.Padding = metrics.Padding;
        _kb.Layout(view.Box.Width, metrics.KeymapHeight, metrics.Padding);
        var room = (int)KeyboardModel.PopupRoom;
        surface.Chrome.Place(new Box(0, view.Box.Height - inset - room, view.Box.Width, inset + room), view.Scale);
        surface.Frame.Matrix = RenderTransform.Translation(0, (1 - surface.Slide.Value) * inset);
        if (immediate && surface.Shown && view.CardInset != inset)
        {
            view.CardInset = inset;
            ApplyCardInset(view);
        }

        _kbModel.Invalidate();
    }

    private void OpenKeyboardOn(LunaView view)
    {
        if (view.Keyboard is not { } surface)
        {
            return;
        }

        surface.Shown = true;
        _kbModel.BarHeight = Fcitx5BarHeight;
        Fcitx5Visible(true);
        UpdateTraceEnabled();
        PlaceKeyboard(view, immediate: false);
        surface.Chrome.Enabled = true;
        surface.Slide.Start(LunaMotion.PositiveSpace, surface.Slide.Value, 1, Now, TimeScale);
        surface.Frame.Matrix = RenderTransform.Translation(0, (1 - surface.Slide.Value) * surface.Height);
        if (!surface.Slide.IsRunning)
        {
            view.CardInset = surface.Height;
            ApplyCardInset(view);
        }

        view.Scheduler?.ScheduleRepaint();
    }

    private void CloseKeyboardOn(LunaView view, bool animate)
    {
        if (view.Keyboard is not { } surface)
        {
            return;
        }

        surface.Shown = false;
        CancelKeyboardTouches();
        Fcitx5Visible(false);
        if (view.CardInset != 0)
        {
            view.CardInset = 0;
            ApplyCardInset(view);
        }

        if (animate)
        {
            surface.Slide.Start(LunaMotion.PositiveSpace, surface.Slide.Value, 0, Now, TimeScale);
        }
        else
        {
            surface.Slide.Jump(0);
        }

        if (!surface.Slide.IsRunning)
        {
            surface.Chrome.Enabled = false;
        }

        view.Scheduler?.ScheduleRepaint();
    }

    private void ApplyCardInset(LunaView view)
    {
        if (view.Deck.ActiveCard is { Fullscreen: false } card)
        {
            SizeCard(card, view);
        }

        if (!view.InCardView)
        {
            LayoutCards(view, animate: false);
        }

        _report.Line($"INSET {view.Output.Name} {view.CardInset}");
    }

    private bool AdvanceKeyboard(LunaView view, long now)
    {
        var candidates = ReferenceEquals(view, _kbView) && (AdvanceCandidates(now) | AdvanceTrace(now));
        if (view.Keyboard is not { } surface || !surface.Slide.Advance(now))
        {
            return candidates;
        }

        surface.Frame.Matrix = RenderTransform.Translation(0, (1 - surface.Slide.Value) * surface.Height);
        if (!surface.Slide.IsRunning)
        {
            if (surface.Shown)
            {
                view.CardInset = surface.Height;
                ApplyCardInset(view);
            }
            else
            {
                surface.Chrome.Enabled = false;
            }
        }

        return surface.Slide.IsRunning || candidates;
    }

    private void SettleKeyboard(LunaView view)
    {
        if (view.Keyboard is { } surface && surface.Slide.IsRunning)
        {
            surface.Slide.Settle();
            AdvanceKeyboard(view, long.MaxValue);
            surface.Frame.Matrix = RenderTransform.Translation(0, (1 - surface.Slide.Value) * surface.Height);
            if (surface.Shown && view.CardInset != surface.Height)
            {
                view.CardInset = surface.Height;
                ApplyCardInset(view);
            }
            else if (!surface.Shown)
            {
                surface.Chrome.Enabled = false;
            }
        }
    }

    private Box AppBoxFor(LunaView view, Card card)
    {
        var box = view.AppBox;
        if (view.CardInset > 0 && ReferenceEquals(card, view.Deck.ActiveCard))
        {
            return box with { Height = Math.Max(1, box.Height - view.CardInset) };
        }

        return box;
    }

    private bool KeymapOrigin(LunaView view, out double x, out double y)
    {
        x = view.Box.X;
        y = 0;
        if (view.Keyboard is not { Shown: true } surface)
        {
            return false;
        }

        y = view.Box.Y + view.Box.Height - (surface.Height * surface.Slide.Value) + _kbModel.BarHeight + _kbModel.Padding;
        return true;
    }

    internal bool KeyboardCovers(double x, double y)
    {
        if (_kbView is not { } view || !KeymapOrigin(view, out var ox, out var oy))
        {
            return false;
        }

        var local = x - view.Box.X;
        return local >= 0 && local < view.Box.Width && y >= oy - _kbModel.Padding - _kbModel.BarHeight && y < view.Box.Y + view.Box.Height;
    }

    internal bool KeyboardTouchDown(int id, double x, double y, uint timeMs)
    {
        if (_kbView is not { } view || !KeymapOrigin(view, out var ox, out var oy))
        {
            return false;
        }

        var now = KeyboardNow;
        var kx = (float)(x - ox);
        var ky = (float)(y - oy);
        var barTop = oy - _kbModel.Padding - _kbModel.BarHeight;
        if (_kbModel.BarHeight > 0 && !_kb.IsLongPressOpen && x >= view.Box.X && y >= barTop && y < oy - _kbModel.Padding)
        {
            var barSlot = KeyboardSlot(id, allocate: true);
            if (barSlot < 0)
            {
                return false;
            }

            _kbTouches[barSlot] = new KeyboardTouch { Id = id, Active = true, Bar = true, X0 = x, Y0 = y, LastX = x, Began = timeMs };
            BarTouchDown();
            return true;
        }

        if (!_kb.Accepts(kx, ky, now))
        {
            return false;
        }

        var slot = KeyboardSlot(id, allocate: true);
        if (slot < 0)
        {
            return false;
        }

        ref var touch = ref _kbTouches[slot];
        touch = new KeyboardTouch
        {
            Id = id,
            Active = true,
            Edge = y >= view.Box.Y + view.Box.Height - _config.EdgeBand,
            X0 = x,
            Y0 = y,
            Began = timeMs,
        };
        touch.Tracker.Reset();
        touch.Tracker.Add(x, y, 0);
        _kb.TouchDown(id, kx, ky, now);
        if (!_kbIdleQueued)
        {
            _kbIdleQueued = true;
            _host.Loop.AddIdle(_kbIdle);
        }

        return true;
    }

    internal void KeyboardTouchMotion(int id, double x, double y, uint timeMs)
    {
        var slot = KeyboardSlot(id, allocate: false);
        if (slot < 0 || _kbView is not { } view)
        {
            return;
        }

        ref var touch = ref _kbTouches[slot];
        if (touch.Bar)
        {
            BarTouchMove(x - touch.LastX);
            touch.LastX = x;
            return;
        }

        touch.Tracker.Add(x, y, (long)(uint)(timeMs - touch.Began) * 1_000_000);
        if (touch.Claimed)
        {
            EdgeMove(x, y, timeMs);
            return;
        }

        if (touch.Edge)
        {
            var inward = touch.Y0 - y;
            var along = x - touch.X0;
            var axis = inward >= KeyboardEdgeTravel && inward >= Math.Abs(along) ? EdgeAxis.Inward
                : Math.Abs(along) >= KeyboardEdgeTravel && Math.Abs(along) > inward ? EdgeAxis.Along
                : (EdgeAxis?)null;
            if (axis is { } claim)
            {
                _kb.Cancel(id);
                _edge = new EdgeState { View = view, Edge = LunaEdge.Bottom };
                if (EdgeClaim(LunaEdge.Bottom, claim, touch.X0, touch.Y0, timeMs))
                {
                    touch.Claimed = true;
                    EdgeMove(x, y, timeMs);
                    return;
                }

                touch.Edge = false;
            }
        }

        if (KeymapOrigin(view, out var ox, out var oy))
        {
            _kb.TouchMotion(id, (float)(x - ox), (float)(y - oy), KeyboardNow);
        }
    }

    internal void KeyboardTouchUp(int id, uint timeMs)
    {
        var slot = KeyboardSlot(id, allocate: false);
        if (slot < 0)
        {
            return;
        }

        ref var touch = ref _kbTouches[slot];
        if (touch.Bar)
        {
            touch.Active = false;
            BarTouchUp(touch.X0, touch.LastX);
            return;
        }

        var claimed = touch.Claimed;
        var (vx, vy) = touch.Tracker.Velocity((long)(uint)(timeMs - touch.Began) * 1_000_000);
        touch.Active = false;
        if (claimed)
        {
            EdgeEnd(vx, vy, timeMs, cancelled: false);
            return;
        }

        _kb.TouchUp(id, KeyboardNow);
    }

    internal void CancelKeyboardTouches()
    {
        if (_kb is null)
        {
            return;
        }

        for (var i = 0; i < _kbTouches.Length; i++)
        {
            if (_kbTouches[i].Active && _kbTouches[i].Claimed)
            {
                EdgeEnd(0, 0, 0, cancelled: true);
            }

            _kbTouches[i].Active = false;
        }

        _kb.CancelAll();
        _kb.Reset();
    }

    private int KeyboardSlot(int id, bool allocate)
    {
        var free = -1;
        for (var i = 0; i < _kbTouches.Length; i++)
        {
            if (_kbTouches[i].Active && _kbTouches[i].Id == id)
            {
                return i;
            }

            if (free < 0 && !_kbTouches[i].Active)
            {
                free = i;
            }
        }

        return allocate ? free : -1;
    }

    private void FlushKeyboard()
    {
        _kb.Frame(KeyboardNow);
    }

    private void OnKeySent(TypedKey key, KeyRoute route)
    {
        if (_kbProbing)
        {
            return;
        }

        _report.Line($"KEY {KeyName(key.Cap, key.Text)} {RouteName(route)}{(key.WordDelete ? " word" : "")}");
    }

    private void OnKeyboardStateChanged()
    {
        _kbModel.Invalidate();
        var shift = _kb.State.Shift;
        if (shift != _kbShiftReported)
        {
            _kbShiftReported = shift;
            if (!_kbProbing)
            {
                _report.Line($"SHIFT {shift.ToString().ToLowerInvariant()}");
            }
        }
    }

    private static string KeyName(KeyCap cap, string text) => cap.Function switch
    {
        KeyFunction.Text when cap.IsSpace => "space",
        KeyFunction.Text => text,
        _ => cap.Name,
    };

    private static string RouteName(KeyRoute route) => route switch
    {
        KeyRoute.Backend => "fcitx5",
        KeyRoute.TextInput => "text-input",
        _ => "keys",
    };

    private void ReportKeyboard()
    {
        if (_kb is null)
        {
            return;
        }

        var height = _kbView is { } view ? KeyboardInset(view) : 0;
        _report.Line($"KEYBOARD {(_kbView is null ? "off" : "on")} {height} layout={_kbSession.CurrentLayout.Id} size={KeyboardConfig.SizeName(_kbSession.Size)} route={RouteName(_kbSession.Router.Route)} {PredictState}");
        RaiseKeyboardEvent(height);
    }

    private void RaiseKeyboardEvent(int height)
    {
        if (_ipc is not { } ipc || !ipc.Events.HasSubscribers(KeyboardEvent))
        {
            return;
        }

        var json = ipc.Events.BeginEvent(KeyboardEvent);
        json.WriteStartObject();
        json.WriteString("state"u8, _kbView is null ? "off" : "on");
        json.WriteNumber("height"u8, height);
        if (_kbView is { } view)
        {
            json.WriteString("output"u8, view.Output.Name);
        }

        json.WriteString("layout"u8, _kbSession.CurrentLayout.Id);
        json.WriteString("size"u8, KeyboardConfig.SizeName(_kbSession.Size));
        json.WriteEndObject();
        ipc.Events.EndEvent();
    }

    private void PlayKeySound(KeyCap cap)
    {
        if (_kbProbing)
        {
            return;
        }

        PlayClip(cap.Function switch
        {
            KeyFunction.Backspace => _kbDelete,
            KeyFunction.Return => _kbReturn,
            KeyFunction.Text when cap.IsSpace => _kbSpace,
            _ => _kbClick,
        });
    }

    private void PlayClip(SoundClip? clip)
    {
        if (!_config.Keyboard.Sounds || Environment.GetEnvironmentVariable("BASIN_MUTE") == "1")
        {
            return;
        }

        if (!EnsureSounds())
        {
            return;
        }

        if (_kbClick is null)
        {
            _kbClick = LoadClip("click_001");
            _kbSpace = LoadClip("click_002");
            _kbDelete = LoadClip("click_003");
            _kbReturn = LoadClip("click_004");
            _kbTick = LoadClip("tick_001");
            clip ??= _kbClick;
        }

        if (clip is not null)
        {
            _soundPlayer!.Play(clip, SoundPlacement.Centered);
        }
    }

    private static string KeyboardStatePath() => Path.Combine(Path.GetDirectoryName(LauncherState.DefaultPath())!, "keyboard.json");

    private void ReloadKeyboard(LunaConfig previous)
    {
        if (_kb is null)
        {
            return;
        }

        if (_config.Keyboard.Fcitx5 != previous.Keyboard.Fcitx5)
        {
            if (_config.Keyboard.Fcitx5)
            {
                StartFcitx5();
            }
            else
            {
                StopFcitx5();
            }
        }

        var savedSize = _kbSession.Size;
        var chosen = _kbSession.SizeChosen;
        _kbSession.Reload(KeyboardOptions());
        if (!chosen)
        {
            _kbModel.Size = _kbSession.Size;
            if (_kbView is { } view)
            {
                PlaceKeyboard(view, immediate: true);
            }
        }
        else if (_config.Keyboard.Size != previous.Keyboard.Size && _config.Keyboard.Size != savedSize)
        {
            _report.Line($"KBSIZE {KeyboardConfig.SizeName(savedSize)} kept keyboard.size={KeyboardConfig.SizeName(_config.Keyboard.Size)}");
        }

        ApplyKeyboard();
    }

    private void PrintKeyboardState()
    {
        if (_kb is null)
        {
            return;
        }

        _report.Line($"  keyboard {(_kbView is null ? "off" : "on")} layout={_kbSession.CurrentLayout.Id} size={KeyboardConfig.SizeName(_kbSession.Size)} shift={_kb.State.Shift.ToString().ToLowerInvariant()} symbol={(_kb.State.SymbolLock ? "lock" : "off")} field={_kbSession.Field.Kind.ToString().ToLowerInvariant()} autocap={(_kb.State.AutoCap ? "on" : "off")} last={_kbSession.LastInput.ToString().ToLowerInvariant()}");
    }

    private int FindKeyboardCell(string name) => _kbSession.Typer.Find(name);

    private long KeyboardStep(int milliseconds) => _kbSession.Typer.Step(milliseconds);

    private bool KeyboardTap(int cell, int stepMs = 600) => _kbView is not null && _kbSession.Typer.TapCell(cell, stepMs);

    private const int KeyboardCommandTouch = 900;

    private void RegisterKeyboardCommands(IpcMethodRegistry methods)
    {
        SoftKeyboardSessionIpc.Register(methods, _report, _kbSession, new SoftKeyboardIpcHooks
        {
            Namespace = "lunawm",
            IsShown = () => _kbView is not null,
            Show = RaiseKeyboard,
            Hide = HideKeyboard,
            Toggle = ToggleKeyboard,
            Report = ReportKeyboard,
        });

        _report.Register(methods, "lunawm/keyboard-candidate", "keyboard-candidate {index:int}",
            Info("Tap candidate N in the keyboard's candidate bar, counted from 0, as fcitx5 last listed them."),
            (ref IpcParams p, IpcReply reply) =>
            {
                var index = (int)p.GetInt("index");
                if (p.Failed)
                {
                    return;
                }

                if (index < 0 || index >= _kbModel.Candidates.Count)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "no such candidate");
                    return;
                }

                ChooseCandidate(index);
            });

        _report.Register(methods, "lunawm/keyboard-trace", "keyboard-trace {word}",
            Info("Run a synthetic trace through the word's key centers, through the engine and the decoder, and commit what the decoder finds."),
            (ref IpcParams p, IpcReply reply) =>
            {
                var word = p.GetString("word");
                if (!p.Failed && !TraceWord(word))
                {
                    reply.Error(IpcErrorCodes.InvalidParams, _kbView is null ? "the keyboard is not shown" : "trace typing is off here, or the word has a letter the layout lacks");
                }
            });
    }

    private string KeyboardMenuLabel => KeyboardShown ? "Hide Keyboard" : "Show Keyboard";
}
