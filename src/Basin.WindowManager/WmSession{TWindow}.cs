namespace Basin.WindowManager;

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

    public WmSession(WmFocusStack<TWindow> focus, Func<WmWindow, TWindow> create)
    {
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(create);

        _focus = focus;
        _create = create;
    }

    public event Action<TWindow>? Adopted;

    public event Action<TWindow>? Forgetting;

    public event Action<TWindow>? Interaction;

    public event Action<TWindow, WmSeat, Edges?>? PointerRequest;

    public IReadOnlyList<TWindow> Windows => _windows;

    public IReadOnlyDictionary<WmWindow, TWindow> ByWindow => _byWindow;

    public TWindow? Lookup(WmWindow window) => _byWindow.GetValueOrDefault(window);

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
