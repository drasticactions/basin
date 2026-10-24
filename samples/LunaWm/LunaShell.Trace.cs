using System.Numerics;
using Basin;
using Basin.SoftKeyboard;

namespace LunaWm;

internal sealed partial class LunaShell
{
    private static readonly MotionSpec TraceFade = new("traceFade", 400, QtCurve.OutCubic);

    private readonly Dictionary<string, TraceDecoder?> _decoders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _decodersLoading = new(StringComparer.Ordinal);
    private CompositorSynchronizationContext? _traceContext;
    private Motion _traceFade;
    private IReadOnlyList<TraceCandidate> _traceCandidates = [];
    private string? _traceCommitted;

    private void AttachTrace()
    {
        _traceContext = new CompositorSynchronizationContext(_host.Loop);
        _kb.TraceStarted += () =>
        {
            _traceFade.Jump(1);
            _kbModel.TraceAlpha = 1;
        };
        _kb.TraceUpdated += _kbModel.Invalidate;
        _kb.TraceEnded += OnTraceEnded;
        _kbSession.TextInput.FieldChanged += _ => UpdateTraceEnabled();
    }

    private void StopTrace()
    {
        _traceContext?.Dispose();
        _traceContext = null;
    }

    private TraceDecoder? DecoderFor(string language)
    {
        if (_decoders.TryGetValue(language, out var decoder))
        {
            return decoder;
        }

        if (_decodersLoading.Add(language) && _traceContext is { } context)
        {
            _ = Task.Run(() =>
            {
                var list = WordList.ForLanguage(language, out var reason);
                var loaded = list is null ? null : new TraceDecoder(list);
                context.Post(_ =>
                {
                    _decodersLoading.Remove(language);
                    _decoders[language] = loaded;
                    if (loaded is null)
                    {
                        _log.Info($"keyboard: no trace typing for '{language}', {reason}");
                    }
                    else
                    {
                        _log.Info($"keyboard: trace typing for '{language}' from {loaded.Words.Source} ({loaded.Words.Count} words)");
                    }

                    UpdateTraceEnabled();
                }, null);
            });
        }

        return null;
    }

    private void UpdateTraceEnabled()
    {
        if (_kb is null)
        {
            return;
        }

        var field = _kbSession.TextInput.Field;
        var wanted = _config.Keyboard.Trace && _kbView is not null &&
            (!field.Active || field.Kind is FieldKind.Text or FieldKind.Terminal);
        var enabled = wanted && DecoderFor(_kbSession.CurrentLayout.Language) is not null;
        _kb.TraceEnabled = enabled;
        _kb.SlowLongPress = enabled;
    }

    private void OnTraceEnded()
    {
        var path = _kb.TracePath;
        _traceFade.Start(TraceFade, 1, 0, Now, TimeScale);
        _kbView?.Scheduler?.ScheduleRepaint();
        if (!_decoders.TryGetValue(_kbSession.CurrentLayout.Language, out var decoder) || decoder is null)
        {
            _report.Line("TRACE none");
            return;
        }

        var results = decoder.Decode(path, _kb.Grid, _kb.Rects);
        if (results.Count == 0)
        {
            _report.Line("TRACE none");
            return;
        }

        CommitTrace(results);
    }

    private void CommitTrace(IReadOnlyList<TraceCandidate> results)
    {
        _traceCandidates = results;
        var best = results[0].Word;
        _traceCommitted = best + " ";
        _kbSession.Router.CommitWord(_traceCommitted, KeyboardNow);
        var alternatives = results.Count > 1 ? $" alt={string.Join(',', results.Skip(1).Take(2).Select(static r => r.Word))}" : "";
        _report.Line($"TRACE {best}{alternatives}");
        if (_kbModel.BarHeight > 0)
        {
            _kbModel.Candidates = [.. results.Select(static r => r.Word)];
            _kbModel.CandidateCursor = 0;
            _kbModel.CandidateScroll = 0;
            _kbModel.Invalidate();
        }
    }

    private bool ChooseTraceCandidate(int index)
    {
        if (_traceCommitted is not { } committed || index < 0 || index >= _traceCandidates.Count)
        {
            return false;
        }

        if (index > 0)
        {
            _kbSession.Router.DeleteCommitted(committed, KeyboardNow);
            _traceCommitted = _traceCandidates[index].Word + " ";
            _kbSession.Router.CommitWord(_traceCommitted, KeyboardNow);
            _report.Line($"TRACE {_traceCandidates[index].Word} replaced");
        }

        ForgetTrace();
        return true;
    }

    private void ForgetTrace()
    {
        _traceCandidates = [];
        _traceCommitted = null;
    }

    private bool AdvanceTrace(long now)
    {
        if (!_traceFade.Advance(now))
        {
            return false;
        }

        _kbModel.TraceAlpha = (float)_traceFade.Value;
        _kbModel.Invalidate();
        return _traceFade.IsRunning;
    }

    private bool TraceWord(string word)
    {
        if (_kbView is null || !_kb.TraceEnabled)
        {
            return false;
        }

        var centers = new List<Vector2>();
        foreach (var c in word)
        {
            var cell = FindKeyboardCell(char.ToLowerInvariant(c).ToString());
            if (cell < 0)
            {
                return false;
            }

            var rect = _kb.Rects[cell];
            centers.Add(new Vector2(rect.CenterX, rect.CenterY));
        }

        var now = KeyboardStep(600);
        _kbSession.NoteInput(LastInput.Touch);
        _kb.TouchDown(KeyboardCommandTouch, centers[0].X, centers[0].Y, now);
        _kb.Frame(now);
        for (var i = 1; i < centers.Count; i++)
        {
            var from = centers[i - 1];
            var to = centers[i];
            var steps = Math.Max(1, (int)(Vector2.Distance(from, to) / 8));
            for (var step = 1; step <= steps; step++)
            {
                var point = Vector2.Lerp(from, to, step / (float)steps);
                _kb.TouchMotion(KeyboardCommandTouch, point.X, point.Y, now += 8);
            }
        }

        if (!_kb.IsTracing)
        {
            var last = centers[^1];
            _kb.TouchMotion(KeyboardCommandTouch, last.X + (float)_config.Keyboard.Timing.TraceThreshold, last.Y, now += 8);
            _kb.TouchMotion(KeyboardCommandTouch, last.X, last.Y, now += 8);
        }

        _kbSession.Typer.AdvanceTo(now);
        _kb.TouchUp(KeyboardCommandTouch, now);
        return true;
    }
}
