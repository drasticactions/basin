using Basin.SoftKeyboard;
using Basin.SoftKeyboard.Fcitx5;

namespace LunaWm;

internal sealed partial class LunaShell
{
    private static readonly MotionSpec CandidateSnap = new("candidateSnap", 500, QtCurve.OutCubic);

    private const int MaxBarCandidates = 100;

    private Fcitx5Keyboard? _fcitx;
    private string _fcitxReason = "fcitx5 is off in [keyboard]";
    private Motion _candidateSnap;
    private double _barTravel;

    private int Fcitx5BarHeight => _fcitx is { BackendPresent: true } ? KeyboardMetrics.CandidateBarHeight : 0;

    private string PredictState => _fcitx switch
    {
        { CandidatesArrived: true } => "predict=fcitx5",
        null => $"predict=none reason={_fcitxReason}",
        { BackendPresent: false } => "predict=none reason=fcitx5 has not taken the keyboard: it is not running, its virtualkeyboard addon is off, or no field is focused",
        _ => "predict=none reason=no candidates yet: fcitx5's keyboard engine gives hints only with EnableHintByDefault",
    };

    private void StartFcitx5()
    {
        if (_fcitx is not null)
        {
            return;
        }

        if (!_config.Keyboard.Enabled || !_config.Keyboard.Fcitx5)
        {
            _fcitxReason = "fcitx5 is off in [keyboard]";
            return;
        }

        _fcitx = Fcitx5Keyboard.TryCreate(_host.Loop, out var reason);
        if (_fcitx is null)
        {
            _fcitxReason = reason ?? "fcitx5 is not reachable";
            _log.Info($"keyboard: no fcitx5 prediction, {_fcitxReason}");
            return;
        }

        _log.Info($"keyboard: owns {Fcitx5Keyboard.BusName}, fcitx5 can compose for it");
        _kbSession.Router.Backend = _fcitx;
        _fcitx.BackendChanged += OnFcitx5Backend;
        _fcitx.CandidatesChanged += OnFcitx5Candidates;
        _fcitx.PreeditChanged += OnFcitx5Preedit;
        _fcitx.InputMethodChanged += name => _report.Line($"FCITX5 im={name ?? "-"}");
        _fcitx.ShowRequested += show => _log.Debug($"keyboard: fcitx5 asks to {(show ? "show" : "hide")} the keyboard, LunaWm's own rule decides");
        SyncFcitx5Field();
    }

    private void StopFcitx5()
    {
        if (_fcitx is not { } fcitx)
        {
            return;
        }

        _kbSession.Router.Backend = null;
        fcitx.Dispose();
        _fcitx = null;
        _fcitxReason = "fcitx5 is off in [keyboard]";
        _kbModel.Candidates = [];
        _kbModel.Preedit = null;
        UpdateCandidateBar();
    }

    private void SyncFcitx5Field()
    {
        if (_fcitx is { } fcitx)
        {
            fcitx.HasFocusedField = _kbSession.TextInput.ActiveSurface is not null && (_imRelay?.HasInputMethodClient ?? false);
        }
    }

    private void Fcitx5Visible(bool visible)
    {
        if (_fcitx is { OwnsName: true } fcitx && (_imRelay?.HasInputMethodClient ?? false))
        {
            fcitx.SetVisible(visible);
        }
    }

    private void OnFcitx5Backend(bool present)
    {
        _report.Line($"FCITX5 backend={(present ? "on" : "off")}");
        UpdateCandidateBar();
    }

    private void UpdateCandidateBar()
    {
        var bar = Fcitx5BarHeight;
        if (Math.Abs(_kbModel.BarHeight - bar) > 0.5)
        {
            _kbModel.BarHeight = bar;
            if (_kbView is { } view)
            {
                PlaceKeyboard(view, immediate: true);
            }

            ReportKeyboard();
        }

        _kbModel.Invalidate();
    }

    private void OnFcitx5Candidates()
    {
        ForgetTrace();
        var candidates = _fcitx!.Candidates;
        _kbModel.Candidates = candidates.Words.Count > MaxBarCandidates ? [.. candidates.Words.Take(MaxBarCandidates)] : candidates.Words;
        _kbModel.CandidateCursor = candidates.Cursor < 0 ? 0 : candidates.Cursor;
        _kbModel.CandidateScroll = 0;
        _candidateSnap.Jump(0);
        _report.Line(candidates.Words.Count > 0
            ? $"CANDIDATES {candidates.Words.Count} first={candidates.Words[0]}"
            : "CANDIDATES 0");
        UpdateCandidateBar();
    }

    private void OnFcitx5Preedit()
    {
        _kbModel.Preedit = _fcitx!.Preedit;
        _kbModel.Invalidate();
    }

    private void ChooseCandidate(int index)
    {
        if (ChooseTraceCandidate(index))
        {
            return;
        }

        if (_fcitx is not { } fcitx || index < 0 || index >= fcitx.Candidates.Words.Count)
        {
            return;
        }

        var word = fcitx.Candidates.Words[index];
        fcitx.SelectCandidate(index);
        _report.Line($"CANDIDATE {index} {word}");
        if (fcitx.InputMethod?.StartsWith("keyboard-", StringComparison.Ordinal) == true)
        {
            _kbSession.Router.Send(new TypedKey(KeyCap.Space, false, false, false, false), KeyboardNow);
        }
    }

    private void BarTouchDown()
    {
        _barTravel = 0;
        _candidateSnap.Jump(_kbModel.CandidateScroll);
    }

    private void BarTouchMove(double dx)
    {
        _barTravel += Math.Abs(dx);
        var limit = Math.Max(0, _kbModel.CandidateContentWidth - _kbModel.Width);
        _kbModel.CandidateScroll = (float)Math.Clamp(_kbModel.CandidateScroll - dx, -60, limit + 60);
        _kbModel.Invalidate();
    }

    private void BarTouchUp(double x0, double x)
    {
        if (_barTravel < LunaShell.TapSlop && _kbView is { } view)
        {
            ChooseCandidate(_kbModel.CandidateAt(x - view.Box.X));
            return;
        }

        var limit = Math.Max(0, _kbModel.CandidateContentWidth - _kbModel.Width);
        var scroll = _kbModel.CandidateScroll;
        if (scroll > limit + 30 && _fcitx?.Candidates.HasNext == true)
        {
            _fcitx.NextPage();
        }
        else if (scroll < -30 && _fcitx?.Candidates.HasPrevious == true)
        {
            _fcitx.PreviousPage();
        }

        var target = Math.Clamp(scroll, 0, limit);
        foreach (var (left, _) in _kbModel.CandidateSpans)
        {
            if (Math.Abs(left - 15 - scroll) < Math.Abs(target - scroll) || target == scroll)
            {
                target = Math.Clamp(left - 15, 0, limit);
            }
        }

        _candidateSnap.Start(CandidateSnap, scroll, target, Now, TimeScale);
        _kbView?.Scheduler?.ScheduleRepaint();
    }

    private bool AdvanceCandidates(long now)
    {
        if (!_candidateSnap.Advance(now))
        {
            return false;
        }

        _kbModel.CandidateScroll = (float)_candidateSnap.Value;
        _kbModel.Invalidate();
        return _candidateSnap.IsRunning;
    }
}
