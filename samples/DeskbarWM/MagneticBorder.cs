using Basin.WindowManager;

namespace DeskbarWm;

internal sealed class MagneticBorder
{
    private const long SnappingDurationMs = 1500;
    private const long SnappingPauseMs = 3000;
    private const float SnapDistance = 8f;

    private long _lastSnapTime;

    public bool AlterDeltaForSnap(Rect screenFrame, Rect frame, ref Point delta, long nowMs)
    {
        if (nowMs - _lastSnapTime > SnappingDurationMs && nowMs - _lastSnapTime < SnappingPauseMs)
        {
            return false;
        }

        var moved = new Rect(frame.X + delta.X, frame.Y + delta.Y, frame.Width, frame.Height);

        var leftDist = Math.Abs(moved.X - screenFrame.X);
        var topDist = Math.Abs(moved.Y - screenFrame.Y);
        var rightDist = Math.Abs(moved.Right - screenFrame.Right);
        var bottomDist = Math.Abs(moved.Bottom - screenFrame.Bottom);

        var snapped = false;
        if (leftDist < SnapDistance || rightDist < SnapDistance)
        {
            snapped = true;
            delta = leftDist < rightDist
                ? delta with { X = screenFrame.X - frame.X }
                : delta with { X = screenFrame.Right - frame.Right };
        }

        if (topDist < SnapDistance || bottomDist < SnapDistance)
        {
            snapped = true;
            delta = topDist < bottomDist
                ? delta with { Y = screenFrame.Y - frame.Y }
                : delta with { Y = screenFrame.Bottom - frame.Bottom };
        }

        if (snapped && nowMs - _lastSnapTime > SnappingPauseMs)
        {
            _lastSnapTime = nowMs;
        }

        return snapped;
    }
}
