namespace EightWm;

internal sealed partial class Shell
{
    internal const int PanMargin = 16;

    private AppWindow? _panApp;
    private double _panFrom;
    private double _panTo;
    private double _panNow;
    private long _panStart;
    private bool _panRunning;

    internal double PanOffset => _panNow;

    internal AppWindow? PanApp => _panApp;

    private void AttachPan()
    {
        _kbSession.FieldChanged += PanForKeyboard;
        _kbSession.TextInputChanged += PanForKeyboard;
    }

    private void PanForKeyboard()
    {
        var target = PanTarget(out var wanted);
        if (!ReferenceEquals(target, _panApp))
        {
            if (_panApp is { } previous)
            {
                SetPan(previous, 0);
            }

            _panApp = target;
            _panNow = 0;
        }

        if (target is null)
        {
            _panRunning = false;
            return;
        }

        if (Math.Abs(wanted - _panTo) < 0.5 && (_panRunning || Math.Abs(_panNow - wanted) < 0.5))
        {
            return;
        }

        _panFrom = _panNow;
        _panTo = wanted;
        _panStart = Environment.TickCount64;
        _panRunning = AnimationsOn;
        if (!_panRunning)
        {
            _panNow = wanted;
            SetPan(target, wanted);
        }

        _report.Line($"PAN {target.AppId} {(int)Math.Round(wanted)}");
        Kick();
    }

    private AppWindow? PanTarget(out double wanted)
    {
        wanted = 0;
        if (!_kbShown || _kbView is not { } view || _kbSession.TextInput.ActiveSurface is not { } surface ||
            OwnerOf(surface) is not { } app || !ReferenceEquals(HomeOf(app), view))
        {
            return _panApp;
        }

        if (_kbSession.TextInput.CaretRectangle is not { } caret)
        {
            return app;
        }

        var caretBottom = app.Cell.Y + caret.Y + caret.Height;
        var overlap = caretBottom - view.KeyboardBox.Y;
        wanted = overlap > 0 ? Math.Min(overlap + PanMargin, view.KeyboardBox.Height) : 0;
        return app;
    }

    private void AdvancePan(ShellView view, long nowMillis)
    {
        if (!_panRunning || _panApp is not { } app || !ReferenceEquals(HomeOf(app), view))
        {
            return;
        }

        var duration = AnimationCatalog.Of(Animation.ShowPanel).DurationMs;
        var t = duration == 0 ? 1 : Math.Clamp((nowMillis - _panStart) / (double)duration, 0, 1);
        _panNow = _panFrom + ((_panTo - _panFrom) * Curves.Evaluate(AnimationCurve.Deceleration, t));
        if (t >= 1)
        {
            _panNow = _panTo;
            _panRunning = false;
            if (_panTo == 0)
            {
                _panApp = null;
            }
        }

        SetPan(app, Math.Round(_panNow));
    }

    private static void SetPan(AppWindow app, double offset)
    {
        if (!app.Frame.IsDestroyed)
        {
            app.Frame.SetPosition(0, -(int)offset);
        }
    }
}
