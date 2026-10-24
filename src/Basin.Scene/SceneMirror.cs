using Pixman;

namespace Basin.Scene;

/// <summary>
/// A node that draws another subtree a second time, clipped to its own rectangle. Mirrors that show mirrors stop after three levels.
/// </summary>
public sealed class SceneMirror : SceneNode
{
    private SceneTree? _source;
    private Scene? _registered;
    private int _width;
    private int _height;
    private int _sourceX;
    private int _sourceY;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneMirror"/> class.
    /// </summary>
    /// <param name="parent">The tree to add the mirror to.</param>
    /// <param name="source">The subtree to draw.</param>
    /// <param name="width">The width of the mirror, in logical pixels.</param>
    /// <param name="height">The height of the mirror, in logical pixels.</param>
    /// <exception cref="InvalidOperationException"><paramref name="source"/> contains the mirror.</exception>
    public SceneMirror(SceneTree parent, SceneTree source, int width, int height)
        : base(parent)
    {
        _width = width;
        _height = height;
        Source = source;
    }

    /// <summary>
    /// Gets or sets the subtree to draw. Damage in the source damages the mirror too. A disabled or destroyed source draws nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">The new source contains the mirror.</exception>
    public SceneTree? Source
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value))
            {
                return;
            }

            if (value is not null)
            {
                for (SceneNode? ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
                {
                    if (ReferenceEquals(ancestor, value))
                    {
                        throw new InvalidOperationException("A mirror cannot draw a subtree that contains it.");
                    }
                }
            }

            DamageSubtree();
            Unregister();
            _source = value;
            Register();
            DamageSubtree();
        }
    }

    /// <summary>
    /// Gets or sets the horizontal offset at which the children of the source draw inside the mirror, in logical pixels.
    /// </summary>
    public int SourceX
    {
        get => _sourceX;
        set => Resize(ref _sourceX, value);
    }

    /// <summary>
    /// Gets or sets the vertical offset at which the children of the source draw inside the mirror, in logical pixels.
    /// </summary>
    public int SourceY
    {
        get => _sourceY;
        set => Resize(ref _sourceY, value);
    }

    /// <summary>
    /// Gets or sets the width of the mirror, in logical pixels.
    /// </summary>
    public int Width
    {
        get => _width;
        set => Resize(ref _width, value);
    }

    /// <summary>
    /// Gets or sets the height of the mirror, in logical pixels.
    /// </summary>
    public int Height
    {
        get => _height;
        set => Resize(ref _height, value);
    }

    /// <summary>
    /// Gets the mirror rectangle in local coordinates, at the origin.
    /// </summary>
    public Box Box => new(0, 0, _width, _height);

    /// <summary>
    /// Gets a value indicating whether the source is set and not destroyed.
    /// </summary>
    public bool HasSource => _source is { IsDestroyed: false };

    /// <inheritdoc/>
    protected override (int Width, int Height) ContentSize => (_width, _height);

    /// <summary>
    /// Detaches the mirror from its source.
    /// </summary>
    protected override void OnDestroy()
    {
        Unregister();
        _source = null;
    }

    private void Register()
    {
        if (_source is not { IsDestroyed: false } source)
        {
            return;
        }

        (source.Mirrors ??= []).Add(this);
        _registered = source.RootOwner();
        _registered?.AddMirror();
    }

    private void Unregister()
    {
        _source?.Mirrors?.Remove(this);
        _registered?.RemoveMirror();
        _registered = null;
    }

    private void Resize(ref int field, int value)
    {
        if (field == value)
        {
            return;
        }

        DamageSubtree();
        field = value;
        DamageSubtree();
    }
}
