using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

/// <summary>
/// The base of every scene graph node. A node has a position relative to its parent, an optional clip, and a lifetime that ends with <see cref="Destroy"/>.
/// </summary>
public abstract class SceneNode
{
    private bool _destroyed;
    private bool _enabled = true;
    private Box _clipBox;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneNode"/> class and appends it to the top of the parent's children.
    /// </summary>
    /// <param name="parent">The tree to add the node to, or <see langword="null"/> for a detached root.</param>
    protected SceneNode(SceneTree? parent)
    {
        Parent = parent;
        parent?.Children.Add(this);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the tree that holds this node. It is <see langword="null"/> for a root and after <see cref="Destroy"/>.
    /// </summary>
    public SceneTree? Parent { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the node and its subtree draw and take input. A change damages the area that the subtree covers.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = true;
                DamageSubtree();
                _enabled = value;
            }
        }
    }

    internal void SetEnabledForCapture(bool value) => _enabled = value;

    /// <summary>
    /// Gets or sets a value indicating whether screen capture must leave out this node and its subtree.
    /// </summary>
    public bool ExcludedFromCapture { get; set; }

    /// <summary>
    /// Gets or sets the name that scene dumps show for this node. It has no effect on rendering.
    /// </summary>
    public string? DebugName { get; set; }

    /// <summary>
    /// Gets the horizontal offset from the parent's origin, in logical pixels.
    /// </summary>
    public int X { get; private set; }

    /// <summary>
    /// Gets the vertical offset from the parent's origin, in logical pixels.
    /// </summary>
    public int Y { get; private set; }

    /// <summary>
    /// Gets the sum of the offsets of this node and its ancestors. It ignores the matrix of any <see cref="SceneTransform"/> on the path.
    /// </summary>
    public (int X, int Y) ScenePosition
    {
        get
        {
            var x = 0;
            var y = 0;
            for (SceneNode? node = this; node is not null; node = node.Parent)
            {
                x += node.X;
                y += node.Y;
            }

            return (x, y);
        }
    }

    /// <summary>
    /// Gets or sets the clip rectangle in the node's local coordinates. An empty box turns clipping off.
    /// </summary>
    public Box ClipBox
    {
        get => _clipBox;
        set
        {
            if (_clipBox.Equals(value))
            {
                return;
            }

            DamageSubtree();
            _clipBox = value;
            DamageSubtree();
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="ClipBox"/> has a positive width and height.
    /// </summary>
    public bool IsClipped => _clipBox.Width > 0 && _clipBox.Height > 0;

    /// <summary>
    /// Gets a value indicating whether <see cref="Destroy"/> has run.
    /// </summary>
    public bool IsDestroyed => _destroyed;

    /// <summary>
    /// Occurs once, at the end of <see cref="Destroy"/>, after the node leaves its parent.
    /// </summary>
    public event Action? Destroyed;

    /// <summary>
    /// Moves the node relative to its parent and damages the old and new areas.
    /// </summary>
    /// <param name="x">The horizontal offset, in logical pixels.</param>
    /// <param name="y">The vertical offset, in logical pixels.</param>
    public void SetPosition(int x, int y)
    {
        if (x == X && y == Y)
        {
            return;
        }

        DamageSubtree();
        X = x;
        Y = y;
        DamageSubtree();
    }

    /// <summary>
    /// Moves the node to the top of another tree's children. It does nothing when the tree is already the parent.
    /// </summary>
    /// <param name="newParent">The tree to move the node into.</param>
    /// <exception cref="InvalidOperationException"><paramref name="newParent"/> is this node or one of its descendants.</exception>
    public void Reparent(SceneTree newParent)
    {
        if (ReferenceEquals(newParent, Parent))
        {
            return;
        }

        for (SceneTree? ancestor = newParent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, this))
            {
                throw new InvalidOperationException("Reparenting would create a cycle.");
            }
        }

        DamageSubtree();
        Parent?.Children.Remove(this);
        Parent = newParent;
        newParent.Children.Add(this);
        DamageSubtree();
    }

    /// <summary>
    /// Moves the node above all of its siblings. It does nothing for a root or for a node that is already on top.
    /// </summary>
    public void RaiseToTop()
    {
        if (Parent is { } parent &&
            parent.Children.Count > 0 &&
            !ReferenceEquals(parent.Children[^1], this) &&
            parent.Children.Remove(this))
        {
            parent.Children.Add(this);
            DamageSubtree();
        }
    }

    /// <summary>
    /// Moves the node below all of its siblings. It does nothing for a root or for a node that is already at the bottom.
    /// </summary>
    public void LowerToBottom()
    {
        if (Parent is { } parent &&
            parent.Children.Count > 0 &&
            !ReferenceEquals(parent.Children[0], this) &&
            parent.Children.Remove(this))
        {
            parent.Children.Insert(0, this);
            DamageSubtree();
        }
    }

    /// <summary>
    /// Moves the node directly above a sibling in the stacking order.
    /// </summary>
    /// <param name="sibling">A different node with the same parent.</param>
    /// <exception cref="InvalidOperationException"><paramref name="sibling"/> is this node or has a different parent.</exception>
    public void PlaceAbove(SceneNode sibling)
    {
        MoveRelativeTo(sibling, offset: 1);
    }

    /// <summary>
    /// Moves the node directly below a sibling in the stacking order.
    /// </summary>
    /// <param name="sibling">A different node with the same parent.</param>
    /// <exception cref="InvalidOperationException"><paramref name="sibling"/> is this node or has a different parent.</exception>
    public void PlaceBelow(SceneNode sibling)
    {
        MoveRelativeTo(sibling, offset: 0);
    }

    /// <summary>
    /// Damages the node's area, runs <see cref="OnDestroy"/>, removes the node from its parent and raises <see cref="Destroyed"/>. A second call does nothing.
    /// </summary>
    public void Destroy()
    {
        if (_destroyed)
        {
            return;
        }

        DamageSubtree();
        _destroyed = true;
        OnDestroy();
        Parent?.Children.Remove(this);
        Parent = null;
        Destroyed?.Invoke();
        BasinCounters.Untrack();
    }

    /// <summary>
    /// Gets the size of the node's own content, in logical pixels. The default is zero, which gives the node no bounds and no damage.
    /// </summary>
    protected virtual (int Width, int Height) ContentSize => (0, 0);

    internal virtual Box SubtreeBounds()
    {
        var (width, height) = ContentSize;
        if (width <= 0 || height <= 0)
        {
            return default;
        }

        var bounds = new Box(0, 0, width, height);
        return IsClipped ? bounds.Intersect(ClipBox) : bounds;
    }

    internal virtual Box UntransformedSubtreeBounds() => SubtreeBounds();

    /// <summary>
    /// Maps a scene point into this node's local coordinates, through the offsets and transforms of every ancestor.
    /// </summary>
    /// <param name="sceneX">The horizontal scene coordinate, in logical pixels.</param>
    /// <param name="sceneY">The vertical scene coordinate, in logical pixels.</param>
    /// <param name="localX">The horizontal local coordinate, or 0 on failure.</param>
    /// <param name="localY">The vertical local coordinate, or 0 on failure.</param>
    /// <returns><see langword="false"/> when a transform on the path cannot be inverted or a mesh deformation does not cover the point.</returns>
    public bool TryMapSceneToLocal(double sceneX, double sceneY, out double localX, out double localY)
    {
        if (!TryMapSceneToParentSpace(sceneX, sceneY, out var x, out var y))
        {
            localX = 0;
            localY = 0;
            return false;
        }

        return TryMapIntoContent(x, y, out localX, out localY);
    }

    private bool TryMapSceneToParentSpace(double sceneX, double sceneY, out double x, out double y)
    {
        if (Parent is not { } parent)
        {
            x = sceneX;
            y = sceneY;
            return true;
        }

        if (!parent.TryMapSceneToParentSpace(sceneX, sceneY, out var parentX, out var parentY))
        {
            x = 0;
            y = 0;
            return false;
        }

        return parent.TryMapIntoContent(parentX, parentY, out x, out y);
    }

    private bool TryMapIntoContent(double x, double y, out double localX, out double localY)
    {
        x -= X;
        y -= Y;
        if (this is SceneTransform transform)
        {
            return transform.TryMapToLocal(x, y, out localX, out localY);
        }

        localX = x;
        localY = y;
        return true;
    }

    private bool ComposeToRoot(
        bool checkVisibility,
        out int x,
        out int y,
        out RenderTransform toScene,
        out bool transformed,
        out Scene? scene,
        out SceneTransform? deformerAncestor)
    {
        x = 0;
        y = 0;
        var matrix = RenderTransform.Identity;
        transformed = false;
        scene = null;
        deformerAncestor = null;
        SceneNode node = this;
        while (true)
        {
            if (checkVisibility && (!node._enabled || node._destroyed))
            {
                toScene = RenderTransform.Identity;
                return false;
            }

            if (!ReferenceEquals(node, this) && deformerAncestor is null &&
                node is SceneTransform { Deformer: not null } deformer)
            {
                deformerAncestor = deformer;
            }

            if (node is SceneTransform frame && !frame.Matrix.IsIdentity)
            {
                matrix = transformed
                    ? RenderTransform.Multiply(
                        frame.Matrix, RenderTransform.Multiply(RenderTransform.Translation(x, y), matrix))
                    : RenderTransform.Multiply(frame.Matrix, RenderTransform.Translation(x, y));
                transformed = true;
                x = 0;
                y = 0;
            }

            x += node.X;
            y += node.Y;
            if (node.Parent is { } parent)
            {
                node = parent;
                continue;
            }

            scene = (node as SceneTree)?.Owner;
            toScene = transformed
                ? RenderTransform.Multiply(RenderTransform.Translation(x, y), matrix)
                : RenderTransform.Identity;
            return true;
        }
    }

    /// <summary>
    /// Maps a box in this node's local coordinates to scene coordinates. A transform on the path gives the bounding box of the transformed area.
    /// </summary>
    /// <param name="local">The box in local logical pixels.</param>
    /// <returns>The box in scene logical pixels. It is empty when a transform cannot map it.</returns>
    public Box MapToScene(in Box local)
    {
        var box = local;
        for (SceneNode? node = this; node is not null && !box.IsEmpty; node = node.Parent)
        {
            if (node is SceneTransform transform)
            {
                if (transform.Deformer is { } deformer)
                {
                    box = deformer.MapBounds(box);
                }

                if (!transform.Matrix.IsIdentity)
                {
                    box = transform.Matrix.TryMapBounds(box, out var hull) ? hull : default;
                }
            }

            box = box.Translated(node.X, node.Y);
        }

        return box;
    }

    internal Scene? RootOwner()
    {
        SceneNode node = this;
        while (node.Parent is { } parent)
        {
            node = parent;
        }

        return (node as SceneTree)?.Owner;
    }

    internal Scene? OwnerIfVisible(out int sceneX, out int sceneY)
    {
        sceneX = 0;
        sceneY = 0;
        var x = 0;
        var y = 0;
        SceneNode node = this;
        while (true)
        {
            if (!node._enabled || node._destroyed)
            {
                return null;
            }

            x += node.X;
            y += node.Y;
            if (node.Parent is { } parent)
            {
                node = parent;
                continue;
            }

            sceneX = x;
            sceneY = y;
            return (node as SceneTree)?.Owner;
        }
    }

    internal Scene? OwnerIfVisibleOrHeld()
    {
        SceneNode node = this;
        while (true)
        {
            if (node._destroyed)
            {
                return null;
            }

            if (!node._enabled)
            {
                return node is SceneTree tree && tree.OwnerIfAttached() is { } scene && scene.HolderOf(tree) is { } holder
                    ? holder.OwnerIfVisible(out _, out _)
                    : null;
            }

            if (node.Parent is { } parent)
            {
                node = parent;
                continue;
            }

            return (node as SceneTree)?.Owner;
        }
    }

    internal Scene? OwnerIfAttached()
    {
        SceneNode node = this;
        while (true)
        {
            if (node._destroyed)
            {
                return null;
            }

            if (node.Parent is { } parent)
            {
                node = parent;
                continue;
            }

            return (node as SceneTree)?.Owner;
        }
    }

    /// <summary>
    /// Damages the whole area that this node and its subtree cover on screen and raises <see cref="Scene.StructureChanged"/>. It does nothing when the node is not visible in a scene.
    /// </summary>
    protected internal void DamageSubtree() => DamageSubtree(structural: true);

    private void DamageSubtree(bool structural)
    {
        if (!ComposeToRoot(
                checkVisibility: true, out var x, out var y, out var toScene, out var transformed, out var owner,
                out var deformerAncestor) ||
            owner is not { } scene)
        {
            return;
        }

        if (structural)
        {
            scene.NotifyStructureChanged();
        }

        if (deformerAncestor is { } deformed)
        {
            deformed.InvalidateCapture();
            deformed.DamageSubtree(structural: false);
            return;
        }

        if (!transformed)
        {
            DamageInto(scene, x, y);
            return;
        }

        var bounds = UntransformedSubtreeBounds();
        if (!bounds.IsEmpty && toScene.TryMapBounds(bounds, out var hull) && !hull.IsEmpty)
        {
            scene.NotifyDamage(this, hull);
        }
    }

    internal virtual void DamageInto(Scene scene, int sceneX, int sceneY)
    {
        var (width, height) = ContentSize;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var box = ClipToOwn(new Box(sceneX, sceneY, width, height), sceneX, sceneY);
        if (!box.IsEmpty)
        {
            scene.NotifyDamage(this, box);
        }
    }

    /// <summary>
    /// Damages part of the node's content. It does nothing when the node is not visible in a scene.
    /// </summary>
    /// <param name="region">The damaged region in local logical pixels. It is clipped to <see cref="ContentSize"/> and <see cref="ClipBox"/>.</param>
    protected void DamageLocal(PixmanRegion32 region)
    {
        if (!ComposeToRoot(
                checkVisibility: true, out var x, out var y, out var toScene, out var transformed, out var owner,
                out var deformerAncestor) ||
            owner is not { } scene)
        {
            return;
        }

        if (deformerAncestor is { } deformed)
        {
            deformed.InvalidateCapture();
            deformed.DamageSubtree(structural: false);
            return;
        }

        var (width, height) = ContentSize;
        var bounds = new Box(0, 0, width, height);
        var clipped = IsClipped ? bounds.Intersect(_clipBox) : bounds;
        if (!transformed)
        {
            scene.NotifyDamage(this, region, x, y, clipped);
            return;
        }

        var extents = region.Extents;
        var local = new Box(extents.X1, extents.Y1, extents.X2 - extents.X1, extents.Y2 - extents.Y1)
            .Intersect(clipped);
        if (!local.IsEmpty && toScene.TryMapBounds(local, out var hull) && !hull.IsEmpty)
        {
            scene.NotifyDamage(this, hull);
        }
    }

    private Box ClipToOwn(in Box box, int sceneX, int sceneY) =>
        IsClipped ? box.Intersect(_clipBox.Translated(sceneX, sceneY)) : box;

    /// <summary>
    /// Releases the node's own resources. <see cref="Destroy"/> calls it once, before the node leaves its parent.
    /// </summary>
    protected virtual void OnDestroy()
    {
    }

    private void MoveRelativeTo(SceneNode sibling, int offset)
    {
        if (Parent is not { } parent || sibling.Parent != parent || ReferenceEquals(sibling, this))
        {
            throw new InvalidOperationException("Nodes must share a parent.");
        }

        var index = parent.Children.IndexOf(this);
        var siblingIndex = parent.Children.IndexOf(sibling);
        if (index == siblingIndex + (offset == 0 ? -1 : 1))
        {
            return;
        }

        parent.Children.Remove(this);
        var anchor = parent.Children.IndexOf(sibling);
        parent.Children.Insert(anchor + offset, this);
        DamageSubtree();
    }
}
