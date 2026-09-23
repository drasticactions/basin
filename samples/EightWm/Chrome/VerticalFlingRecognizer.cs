using Avalonia;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.VisualTree;
using AvaWin.Controls;

namespace EightWm;

public sealed class VerticalFlingRecognizer : GestureRecognizer
{
    private const double MinimumSpeed = 200;
    private const ulong WindowMillis = 100;

    private readonly List<(ulong Time, Point Position)> _samples = [];
    private IPointer? _tracking;

    public event Action<double>? Flung;

    public bool IgnoreItems { get; init; }

    protected override void PointerPressed(PointerPressedEventArgs e)
    {
        _samples.Clear();
        _tracking = null;
        if (IgnoreItems && e.Source is Visual source && (source is ListViewItem || source.FindAncestorOfType<ListViewItem>() is not null))
        {
            return;
        }

        _tracking = e.Pointer;
        _samples.Add((e.Timestamp, e.GetPosition(null)));
    }

    protected override void PointerMoved(PointerEventArgs e)
    {
        if (e.Pointer != _tracking)
        {
            return;
        }

        _samples.Add((e.Timestamp, e.GetPosition(null)));
        while (_samples.Count > 2 && e.Timestamp - _samples[0].Time > WindowMillis)
        {
            _samples.RemoveAt(0);
        }
    }

    protected override void PointerReleased(PointerReleasedEventArgs e)
    {
        if (e.Pointer != _tracking)
        {
            return;
        }

        _tracking = null;
        _samples.Add((e.Timestamp, e.GetPosition(null)));
        var first = _samples[0];
        var last = _samples[^1];
        _samples.Clear();
        var seconds = (last.Time - first.Time) / 1000.0;
        if (seconds <= 0)
        {
            return;
        }

        var vx = (last.Position.X - first.Position.X) / seconds;
        var vy = (last.Position.Y - first.Position.Y) / seconds;
        if (Math.Abs(vy) > MinimumSpeed && Math.Abs(vy) > Math.Abs(vx))
        {
            Flung?.Invoke(vy);
        }
    }

    protected override void PointerCaptureLost(IPointer pointer)
    {
        if (pointer == _tracking)
        {
            _tracking = null;
            _samples.Clear();
        }
    }
}
