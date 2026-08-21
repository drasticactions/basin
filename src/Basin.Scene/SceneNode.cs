using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

public abstract class SceneNode
{
    private bool _destroyed;
    private bool _enabled = true;
    private Box _clipBox;

    protected SceneNode(SceneTree? parent)
    {
        Parent = parent;
        parent?.Children.Add(this);
        BasinCounters.Track();
    }

    public SceneTree? Parent { get; private set; }

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

    public int X { get; private set; }

    public int Y { get; private set; }

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

    public bool IsClipped => _clipBox.Width > 0 && _clipBox.Height > 0;

    public bool IsDestroyed => _destroyed;

    public event Action? Destroyed;

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

    public void PlaceAbove(SceneNode sibling)
    {
        MoveRelativeTo(sibling, offset: 1);
    }

    public void PlaceBelow(SceneNode sibling)
    {
        MoveRelativeTo(sibling, offset: 0);
    }

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

    public bool TryMapSceneToLocal(double sceneX, double sceneY, out double localX, out double localY)
    {
        ComposeToRoot(checkVisibility: false, out var x, out var y, out var toScene, out var transformed, out _, out _);
        if (!transformed)
        {
            localX = sceneX - x;
            localY = sceneY - y;
            return true;
        }

        if (!toScene.TryInvert(out var inverse))
        {
            localX = 0;
            localY = 0;
            return false;
        }

        (localX, localY) = inverse.Map(sceneX, sceneY);
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

    protected internal void DamageSubtree()
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
            deformed.DamageSubtree();
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
            deformed.DamageSubtree();
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

    protected virtual void OnDestroy()
    {
    }

    private void MoveRelativeTo(SceneNode sibling, int offset)
    {
        if (Parent is not { } parent || sibling.Parent != parent || ReferenceEquals(sibling, this))
        {
            throw new InvalidOperationException("Nodes must share a parent.");
        }

        parent.Children.Remove(this);
        var anchor = parent.Children.IndexOf(sibling);
        parent.Children.Insert(anchor + offset, this);
        DamageSubtree();
    }
}
