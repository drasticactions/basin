namespace Basin.WindowManager;

/// <summary>
/// The window table of a manager. It wraps each new window, forgets closed ones, and queues seat interactions and client pointer requests for the manager to drain.
/// </summary>
/// <typeparam name="TWindow">The manager's own window wrapper.</typeparam>
public sealed class WmSession<TWindow>
    where TWindow : class, IWmManagedWindow
{
    private readonly WmFocusStack<TWindow> _focus;
    private readonly Func<WmWindow, TWindow> _create;
    private readonly List<TWindow> _windows = [];
    private readonly Dictionary<WmWindow, TWindow> _byWindow = [];
    private readonly Queue<WmWindow> _interactions = new();
    private readonly Queue<(TWindow Window, WmSeat Seat, Edges? Edges)> _pointerRequests = new();
    private readonly HashSet<WmSeat> _seenSeats = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="WmSession{TWindow}"/> class.
    /// </summary>
    /// <param name="focus">The focus stack that forgets closed windows with this table.</param>
    /// <param name="create">The factory that wraps each new window.</param>
    /// <exception cref="ArgumentNullException"><paramref name="focus"/> or <paramref name="create"/> is <see langword="null"/>.</exception>
    public WmSession(WmFocusStack<TWindow> focus, Func<WmWindow, TWindow> create)
    {
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(create);

        _focus = focus;
        _create = create;
    }

    /// <summary>
    /// Occurs in <see cref="AdoptNewWindows"/> for each wrapper, after it is in the table.
    /// </summary>
    public event Action<TWindow>? Adopted;

    /// <summary>
    /// Occurs in <see cref="ForgetClosedWindows"/> for each wrapper, before it leaves the table and the focus stack.
    /// </summary>
    public event Action<TWindow>? Forgetting;

    /// <summary>
    /// Occurs in <see cref="DrainInteractions"/> for each queued interaction with a window that is still open.
    /// </summary>
    public event Action<TWindow>? Interaction;

    /// <summary>
    /// Occurs in <see cref="DrainPointerRequests"/> for each queued client request for a move or resize. The edges are <see langword="null"/> for a move.
    /// </summary>
    public event Action<TWindow, WmSeat, Edges?>? PointerRequest;

    /// <summary>
    /// Gets the wrappers in the order they were adopted.
    /// </summary>
    public IReadOnlyList<TWindow> Windows => _windows;

    /// <summary>
    /// Gets the wrapper of each library window.
    /// </summary>
    public IReadOnlyDictionary<WmWindow, TWindow> ByWindow => _byWindow;

    /// <summary>
    /// Gets the wrapper of a window's parent.
    /// </summary>
    /// <param name="window">The child window.</param>
    /// <returns>The parent's wrapper, or <see langword="null"/> when the window has no parent or the parent is not in the table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public TWindow? ParentOf(TWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.Window.Parent is { } parent ? ByWindow.GetValueOrDefault(parent) : null;
    }

    /// <summary>
    /// Gets the wrapper of a library window.
    /// </summary>
    /// <param name="window">The library window.</param>
    /// <returns>The wrapper, or <see langword="null"/> when the window is not in the table.</returns>
    public TWindow? Lookup(WmWindow window) => _byWindow.GetValueOrDefault(window);

    /// <summary>
    /// Subscribes to the interactions of each seat once, however often the seat appears. A removed seat leaves the set.
    /// </summary>
    /// <param name="context">The manage sequence in progress.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public void ObserveSeats(ManageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var seat in context.Seats)
        {
            if (!_seenSeats.Add(seat))
            {
                continue;
            }

            var observed = seat;
            observed.WindowInteraction += _interactions.Enqueue;
            observed.Removed += () => _seenSeats.Remove(observed);
        }
    }

    /// <summary>
    /// Wraps each window in <see cref="ManageContext.NewWindows"/>, adds it to the table, and queues its move and resize requests.
    /// </summary>
    /// <param name="context">The manage sequence in progress.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public void AdoptNewWindows(ManageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var window in context.NewWindows)
        {
            var adopted = _create(window);
            _windows.Add(adopted);
            _byWindow[window] = adopted;

            window.PointerMoveRequested += seat => _pointerRequests.Enqueue((adopted, seat, null));
            window.PointerResizeRequested += (seat, edges) => _pointerRequests.Enqueue((adopted, seat, edges));

            Adopted?.Invoke(adopted);
        }
    }

    /// <summary>
    /// Removes each window in <see cref="ManageContext.ClosedWindows"/> from the table and from the focus stack.
    /// </summary>
    /// <param name="context">The manage sequence in progress.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public void ForgetClosedWindows(ManageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var window in context.ClosedWindows)
        {
            if (!_byWindow.Remove(window, out var forgotten))
            {
                continue;
            }

            Forgetting?.Invoke(forgotten);
            _windows.Remove(forgotten);
            _focus.Forget(forgotten);
        }
    }

    /// <summary>
    /// Raises <see cref="Interaction"/> for each queued interaction and empties the queue. It skips windows that closed or are not in the table.
    /// </summary>
    public void DrainInteractions()
    {
        while (_interactions.TryDequeue(out var window))
        {
            if (_byWindow.TryGetValue(window, out var interacted) && !interacted.Window.IsClosed)
            {
                Interaction?.Invoke(interacted);
            }
        }
    }

    /// <summary>
    /// Raises <see cref="PointerRequest"/> for each queued request and empties the queue. It skips requests whose window closed or whose seat was removed.
    /// </summary>
    public void DrainPointerRequests()
    {
        while (_pointerRequests.TryDequeue(out var request))
        {
            var (window, seat, edges) = request;
            if (window.Window.IsClosed || seat.IsRemoved)
            {
                continue;
            }

            PointerRequest?.Invoke(window, seat, edges);
        }
    }
}
