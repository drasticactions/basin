namespace Basin.WindowManager;

/// <summary>
/// An interactive move or resize on top of a <see cref="PointerOperation"/>. The drag computes positions and sizes, and the manager applies them.
/// </summary>
public sealed class WmDrag
{
    private Size? _lastProposed;

    private WmDrag(WmWindow window, PointerOperation op, Edges edges, Rect start)
    {
        Window = window;
        Op = op;
        Edges = edges;
        Start = start;
    }

    /// <summary>
    /// Gets the window that the drag moves or resizes.
    /// </summary>
    public WmWindow Window { get; }

    /// <summary>
    /// Gets the pointer operation that drives the drag.
    /// </summary>
    public PointerOperation Op { get; }

    /// <summary>
    /// Gets the edges that a resize moves. It is <see cref="Edges.None"/> for a move.
    /// </summary>
    public Edges Edges { get; }

    /// <summary>
    /// Gets the window rectangle at the start of the drag, in the compositor's logical space.
    /// </summary>
    public Rect Start { get; }

    /// <summary>
    /// Gets a value indicating whether the drag is a resize.
    /// </summary>
    public bool IsResize => Edges != Edges.None;

    /// <summary>
    /// Gets a value indicating whether the operation continues and the window is open.
    /// </summary>
    public bool IsActive => !Op.IsEnded && !Window.IsClosed;

    /// <summary>
    /// Gets a value indicating whether the buttons came up while the operation continues.
    /// </summary>
    public bool IsReleased => !Op.IsEnded && Op.IsReleased;

    /// <summary>
    /// Gets the last size given to <see cref="Propose"/>, or zero before the first call.
    /// </summary>
    public Size Desired { get; private set; }

    /// <summary>
    /// Starts a drag on a seat, and tells the window that a resize starts when edges are given.
    /// </summary>
    /// <param name="window">The window to move or resize.</param>
    /// <param name="seat">The seat whose pointer drives the drag.</param>
    /// <param name="edges">The edges to resize, or <see cref="Edges.None"/> for a move.</param>
    /// <param name="start">The window rectangle at the start, in the compositor's logical space.</param>
    /// <returns>The new drag. It shares the operation that is already in progress on the seat, if there is one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> or <paramref name="seat"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public static WmDrag Begin(WmWindow window, WmSeat seat, Edges edges, Rect start)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(seat);
        var drag = new WmDrag(window, seat.StartPointerOperation(), edges, start);
        if (edges != Edges.None)
        {
            window.InformResizing(true);
        }

        return drag;
    }

    /// <summary>
    /// Picks the corner of a window nearest the pointer, for a resize with no edges given.
    /// </summary>
    /// <param name="window">The window rectangle.</param>
    /// <param name="pointer">The pointer position in the same space.</param>
    /// <returns>One horizontal and one vertical edge.</returns>
    public static Edges Quadrant(Rect window, Point pointer) =>
        (pointer.X < window.X + (window.Width / 2) ? Edges.Left : Edges.Right)
        | (pointer.Y < window.Y + (window.Height / 2) ? Edges.Top : Edges.Bottom);

    /// <summary>
    /// Gets the window position for a move.
    /// </summary>
    /// <param name="delta">The total pointer motion, usually <see cref="PointerOperation.Delta"/>.</param>
    /// <returns>The start position moved by the delta.</returns>
    public Point MovedTo(Point delta) => new(Start.X + delta.X, Start.Y + delta.Y);

    /// <summary>
    /// Gets the window size for a resize. Only the dragged edges move, and each component stays at least the window's minimum and at least 1.
    /// </summary>
    /// <param name="delta">The total pointer motion, usually <see cref="PointerOperation.Delta"/>.</param>
    /// <returns>The start size changed by the delta on the dragged edges.</returns>
    public Size SizeFor(Point delta)
    {
        var minimum = Window.SizeHint.Minimum;
        var minWidth = Math.Max(minimum.Width, 1);
        var minHeight = Math.Max(minimum.Height, 1);
        var width = Start.Width;
        var height = Start.Height;
        if ((Edges & Edges.Left) != 0)
        {
            width = Math.Max(Start.Width - delta.X, minWidth);
        }
        else if ((Edges & Edges.Right) != 0)
        {
            width = Math.Max(Start.Width + delta.X, minWidth);
        }

        if ((Edges & Edges.Top) != 0)
        {
            height = Math.Max(Start.Height - delta.Y, minHeight);
        }
        else if ((Edges & Edges.Bottom) != 0)
        {
            height = Math.Max(Start.Height + delta.Y, minHeight);
        }

        return new Size(width, height);
    }

    /// <summary>
    /// Gets the window position that keeps the far edges still, for the size the client committed.
    /// </summary>
    /// <param name="committed">The size that the client took, not the size proposed.</param>
    /// <returns>The position for the left or top edge when that edge is dragged, else the start position.</returns>
    public Point AnchoredAt(Size committed) => new(
        (Edges & Edges.Left) != 0 ? Start.X + (Start.Width - committed.Width) : Start.X,
        (Edges & Edges.Top) != 0 ? Start.Y + (Start.Height - committed.Height) : Start.Y);

    /// <summary>
    /// Records a desired size and proposes it to the window, unless it equals the size proposed last.
    /// </summary>
    /// <param name="size">The size in logical pixels.</param>
    /// <exception cref="InvalidOperationException">The size is new and no manage sequence is in progress.</exception>
    public void Propose(Size size)
    {
        Desired = size;
        if (_lastProposed == size)
        {
            return;
        }

        _lastProposed = size;
        Window.ProposeDimensions(size);
    }

    /// <summary>
    /// Ends the drag. For a resize of an open window, it tells the window the resize ended and proposes the desired size if it was never sent.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void Finish()
    {
        if (IsResize && !Window.IsClosed)
        {
            Window.InformResizing(false);
            if (!Desired.IsEmpty && _lastProposed != Desired)
            {
                Window.ProposeDimensions(Desired);
            }
        }

        Op.End();
    }
}
