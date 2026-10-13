namespace LunaWm;

internal sealed partial class LunaShell
{
    private void AllocationProbe(string path, int frames)
    {
        if (path is "keypress" or "repeat")
        {
            KeyboardProbe(path, frames);
            return;
        }

        var view = CommandView;
        if (view.Deck.IsEmpty)
        {
            _report.Line("ALLOCPROBE none");
            return;
        }

        if (!view.InCardView)
        {
            EnterCardView(view);
        }

        SettleAll();
        var x = view.Box.X + (view.Box.Width / 2.0);
        var y = view.Box.Y + (view.Metrics.Origin * 0.9);
        var time = (uint)Environment.TickCount;
        long bytes;
        switch (path)
        {
            case "drag":
                if (!ShellPress(90, x, y, time, Basin.InputCodes.BtnLeft))
                {
                    _report.Line("ALLOCPROBE drag missed");
                    return;
                }

                for (var warm = 1; warm <= 30; warm++)
                {
                    ShellMove(90, x + (warm * 3), y, time + (uint)(warm * 8));
                    Advance(view);
                }

                bytes = GC.GetAllocatedBytesForCurrentThread();
                for (var frame = 0; frame < frames; frame++)
                {
                    var offset = 90 + (Math.Sin(frame * 0.05) * 60);
                    ShellMove(90, x + offset, y, time + (uint)(240 + (frame * 8)));
                    Advance(view);
                }

                bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                ShellCancel(90);
                break;
            case "fling":
                ShellPress(91, x, y, time, Basin.InputCodes.BtnLeft);
                for (var step = 1; step <= 6; step++)
                {
                    ShellMove(91, x - (step * 40), y, time + (uint)(step * 8));
                }

                ShellRelease(91, time + 56);
                for (var warm = 0; warm < 3; warm++)
                {
                    Advance(view);
                }

                bytes = GC.GetAllocatedBytesForCurrentThread();
                var start = Environment.TickCount64;
                var advanced = 0;
                while (advanced < frames && Environment.TickCount64 - start < 2000)
                {
                    Advance(view);
                    advanced++;
                }

                bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                frames = advanced;
                break;
            default:
                _report.Line($"ALLOCPROBE unknown {path}");
                return;
        }

        _report.Line($"ALLOCPROBE {path} frames={frames} bytes={bytes}");
    }

    private void KeyboardProbe(string path, int frames)
    {
        if (_kbView is not { } view || !KeymapOrigin(view, out var ox, out var oy))
        {
            _report.Line("ALLOCPROBE keyboard-hidden");
            return;
        }

        SettleAll();
        var rects = _kb.Rects;
        var a = rects[FindKeyboardCell("a")];
        var back = rects[FindKeyboardCell("backspace")];
        var time = (uint)Environment.TickCount;
        _kbProbing = true;
        try
        {
            long bytes;
            if (path == "keypress")
            {
                void Press(int frame)
                {
                    var t = time + (uint)(frame * 16);
                    KeyboardTouchDown(77, ox + a.CenterX, oy + a.CenterY, t);
                    FlushKeyboard();
                    KeyboardTouchMotion(77, ox + a.CenterX + 10, oy + a.CenterY + 4, t + 4);
                    Advance(view);
                    KeyboardTouchUp(77, t + 8);
                    Advance(view);
                }

                for (var warm = 0; warm < 30; warm++)
                {
                    Press(warm);
                }

                bytes = GC.GetAllocatedBytesForCurrentThread();
                for (var frame = 0; frame < frames; frame++)
                {
                    Press(30 + frame);
                }

                bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            }
            else
            {
                KeyboardTouchDown(78, ox + back.CenterX, oy + back.CenterY, time);
                FlushKeyboard();
                var now = KeyboardNow;
                for (var warm = 0; warm < 30; warm++)
                {
                    now = _kb.NextDeadline;
                    _kb.Tick(now);
                    Advance(view);
                }

                bytes = GC.GetAllocatedBytesForCurrentThread();
                for (var frame = 0; frame < frames; frame++)
                {
                    now = _kb.NextDeadline;
                    _kb.Tick(now);
                    Advance(view);
                }

                bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                _kbSession.Typer.AdvanceTo(now);
                _kb.Cancel(78);
                KeyboardTouchUp(78, time);
            }

            _report.Line($"ALLOCPROBE {path} frames={frames} bytes={bytes}");
        }
        finally
        {
            _kbProbing = false;
        }
    }
}
