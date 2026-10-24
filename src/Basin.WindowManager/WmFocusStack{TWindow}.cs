using System.Collections;

namespace Basin.WindowManager;

/// <summary>
/// The focus order of a manager's windows, most recent first, and the seat focus that follows it.
/// </summary>
/// <typeparam name="TWindow">The manager's own window wrapper.</typeparam>
public sealed class WmFocusStack<TWindow> : IReadOnlyList<TWindow>
    where TWindow : class, IWmManagedWindow
{
    private readonly RiverWindowManager _wm;
    private readonly List<TWindow> _stack = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="WmFocusStack{TWindow}"/> class.
    /// </summary>
    /// <param name="wm">The manager whose layer shell this stack checks for exclusive focus.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wm"/> is <see langword="null"/>.</exception>
    public WmFocusStack(RiverWindowManager wm)
    {
        ArgumentNullException.ThrowIfNull(wm);
        _wm = wm;
    }

    /// <summary>
    /// Occurs in <see cref="Focus"/> before the stack and the seat change.
    /// </summary>
    public event Action<TWindow>? Focusing;

    /// <summary>
    /// Occurs in <see cref="Focus"/> after the stack and the seat changed.
    /// </summary>
    public event Action<TWindow>? FocusChanged;

    /// <summary>
    /// Gets or sets the focused window. A direct set changes neither the stack nor the seat.
    /// </summary>
    public TWindow? Focused { get; set; }

    /// <summary>
    /// Gets or sets the seat that gets keyboard focus, or <see langword="null"/> to change no seat.
    /// </summary>
    public WmSeat? Seat { get; set; }

    /// <inheritdoc/>
    public int Count => _stack.Count;

    /// <inheritdoc/>
    public TWindow this[int index] => _stack[index];

    /// <summary>
    /// Moves a window to the top of the stack and focuses it on <see cref="Seat"/>. The seat does not change while a layer surface holds exclusive focus.
    /// </summary>
    /// <param name="window">The window to focus.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The seat must change and no manage sequence is in progress.</exception>
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

    /// <summary>
    /// Focuses no window and clears the seat focus. The stack order does not change.
    /// </summary>
    /// <exception cref="InvalidOperationException">A seat is set and no manage sequence is in progress.</exception>
    public void ClearFocus()
    {
        Focused = null;
        if (Seat is { IsRemoved: false } seat)
        {
            seat.ClearFocus();
        }
    }

    /// <summary>
    /// Focuses the most recent open window that a predicate accepts.
    /// </summary>
    /// <param name="eligible">The test for each window, in stack order.</param>
    /// <returns><see langword="true"/> when a window got focus.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eligible"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Removes a window from the stack. When it was focused, focus moves to the next window, or is cleared when the stack is empty.
    /// </summary>
    /// <param name="window">The window to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
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

    /// <inheritdoc/>
    public IEnumerator<TWindow> GetEnumerator() => _stack.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
