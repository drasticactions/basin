using System.Runtime.CompilerServices;
using Basin.Capabilities;
using Prowl.PaperUI.LayoutEngine;

namespace Basin.UI.Paper;

/// <summary>
/// Describes one Paper surface to a screen reader. Widgets record their nodes while a frame is built,
/// and after layout the recorder gives each node its box in layout coordinates.
/// It raises <see cref="Changed"/> only when a node, its state or its box differs from the last frame.
/// </summary>
public sealed class PaperAccessibility : IAccessibleChrome, IDisposable
{
    private static readonly ConditionalWeakTable<Prowl.PaperUI.Paper, PaperAccessibility> Attached = new();

    private readonly PaperSurface _surface;
    private readonly Prowl.PaperUI.Paper _paper;
    private readonly string _name;
    private readonly List<(ElementHandle Handle, AccessibleNode Node, int Parent)> _pending = [];
    private List<(AccessibleNode Node, int Parent)> _current = [];
    private List<(AccessibleNode Node, int Parent)> _next = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaperAccessibility"/> class and attaches it to the surface's Paper instance.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <param name="name">The name of the surface's window, for example "Settings".</param>
    public PaperAccessibility(PaperSurface surface, string name)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
        _paper = surface.Paper;
        _name = name;
        Attached.AddOrUpdate(_paper, this);
        _paper.OnEndOfFramePostLayout += Resolve;
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>
    /// Gets the recorder attached to a Paper instance, or <see langword="null"/> when no screen reader listens.
    /// </summary>
    /// <param name="paper">The Paper instance that builds the frame.</param>
    /// <returns>The recorder, or <see langword="null"/>.</returns>
    public static PaperAccessibility? For(Prowl.PaperUI.Paper paper) => Attached.TryGetValue(paper, out var found) && !found._disposed ? found : null;

    /// <summary>
    /// Records a node for the element that a widget just built. Call it during the build of a frame.
    /// </summary>
    /// <param name="handle">The element that the node covers.</param>
    /// <param name="role">The role.</param>
    /// <param name="name">The name, or <see langword="null"/>.</param>
    /// <param name="states">The states.</param>
    /// <param name="value">The value as text, or <see langword="null"/>.</param>
    /// <param name="press">Runs on the compositor loop when the reader presses the node, or <see langword="null"/>. The surface redraws after it.</param>
    /// <param name="parent">The index that an earlier call returned, or -1 for a node directly in the surface's window.</param>
    /// <returns>The index of the node, for children.</returns>
    public int Record(ElementHandle handle, AccessibleRole role, string? name, AccessibleStates states = AccessibleStates.None, string? value = null, Action? press = null, int parent = -1)
    {
        var redrawn = press is null ? null : Redrawing(press);
        _pending.Add((handle, new AccessibleNode(role, name, default, states, value, redrawn), parent));
        return _pending.Count - 1;
    }

    /// <summary>
    /// Writes the surface as a window with the nodes of the last frame.
    /// </summary>
    /// <param name="tree">The builder to write into.</param>
    public void Describe(AccessibleTreeBuilder tree)
    {
        if (_disposed)
        {
            return;
        }

        var size = _surface.Size;
        var window = tree.Add(-1, new AccessibleNode(AccessibleRole.Window, _name, new Box((int)_surface.PositionX, (int)_surface.PositionY, size.Width, size.Height)));
        var first = tree.Count;
        foreach (var (node, parent) in _current)
        {
            tree.Add(parent < 0 ? window : first + parent, node);
        }
    }

    /// <summary>
    /// Detaches the recorder from the Paper instance. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _paper.OnEndOfFramePostLayout -= Resolve;
        Attached.Remove(_paper);
        _pending.Clear();
    }

    private Action Redrawing(Action press) => () =>
    {
        press();
        _surface.Invalidate();
    };

    private void Resolve()
    {
        if (_disposed)
        {
            return;
        }

        _next.Clear();
        var originX = _surface.PositionX;
        var originY = _surface.PositionY;
        foreach (var (handle, node, parent) in _pending)
        {
            ref var data = ref handle.Data;
            var box = new Box((int)Math.Round(originX + data.X), (int)Math.Round(originY + data.Y), (int)Math.Round(data.LayoutWidth), (int)Math.Round(data.LayoutHeight));
            _next.Add((node with { Bounds = box }, parent));
        }

        _pending.Clear();
        if (Same(_current, _next))
        {
            return;
        }

        (_current, _next) = (_next, _current);
        Changed?.Invoke();
    }

    private static bool Same(List<(AccessibleNode Node, int Parent)> a, List<(AccessibleNode Node, int Parent)> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            var (x, xp) = a[i];
            var (y, yp) = b[i];
            if (xp != yp || x.Role != y.Role || x.Name != y.Name || x.Value != y.Value || x.States != y.States || x.Bounds != y.Bounds || (x.Press is null) != (y.Press is null))
            {
                return false;
            }
        }

        return true;
    }
}
