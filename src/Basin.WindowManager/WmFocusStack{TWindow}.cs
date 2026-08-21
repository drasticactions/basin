using System.Collections;

namespace Basin.WindowManager;

public sealed class WmFocusStack<TWindow> : IReadOnlyList<TWindow>
    where TWindow : class, IWmManagedWindow
{
    private readonly RiverWindowManager _wm;
    private readonly List<TWindow> _stack = [];

    public WmFocusStack(RiverWindowManager wm)
    {
        ArgumentNullException.ThrowIfNull(wm);
        _wm = wm;
    }

    public event Action<TWindow>? Focusing;

    public event Action<TWindow>? FocusChanged;

    public TWindow? Focused { get; set; }

    public WmSeat? Seat { get; set; }

    public int Count => _stack.Count;

    public TWindow this[int index] => _stack[index];

    public void Focus(TWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        Focusing?.Invoke(window);
        _stack.Remove(window);
        _stack.Insert(0, window);
        Focused = window;

        if (Seat is { IsRemoved: false } seat && _wm.LayerShell?.HasExclusiveFocus(seat) != true)
        {
            seat.FocusWindow(window.Window);
        }

        FocusChanged?.Invoke(window);
    }

    public void ClearFocus()
    {
        Focused = null;
        if (Seat is { IsRemoved: false } seat)
        {
            seat.ClearFocus();
        }
    }

    public bool RestoreFromStack(Func<TWindow, bool> eligible)
    {
        ArgumentNullException.ThrowIfNull(eligible);

        foreach (var window in _stack)
        {
            if (!window.Window.IsClosed && eligible(window))
            {
                Focus(window);
                return true;
            }
        }

        return false;
    }

    public void Forget(TWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _stack.Remove(window);
        if (!ReferenceEquals(Focused, window))
        {
            return;
        }

        if (_stack.Count > 0)
        {
            Focus(_stack[0]);
        }
        else
        {
            ClearFocus();
        }
    }

    public IEnumerator<TWindow> GetEnumerator() => _stack.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
