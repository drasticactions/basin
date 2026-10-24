namespace Basin.Scene;

/// <summary>
/// A node that draws triangles from an <see cref="IMeshSource"/>, optionally textured with a sprite buffer.
/// </summary>
public sealed class SceneMesh : SceneNode
{
    private Box _bounds;
    private IMeshSource? _source;
    private BufferLock _sprite;
    private ITexture? _texture;
    private IRenderer? _textureRenderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneMesh"/> class with no source and empty bounds.
    /// </summary>
    /// <param name="parent">The tree to add the node to.</param>
    public SceneMesh(SceneTree parent)
        : base(parent)
    {
    }

    /// <summary>
    /// Gets or sets the source of the triangles. The node draws nothing while it is <see langword="null"/>.
    /// </summary>
    public IMeshSource? Source
    {
        get => _source;
        set
        {
            if (!ReferenceEquals(_source, value))
            {
                DamageSubtree();
                _source = value;
                DamageSubtree();
            }
        }
    }

    /// <summary>
    /// Gets or sets the area that the mesh covers, in local logical pixels. Damage uses it, so every vertex must stay inside it.
    /// </summary>
    public Box Bounds
    {
        get => _bounds;
        set
        {
            if (_bounds != value)
            {
                DamageSubtree();
                _bounds = value;
                DamageSubtree();
            }
        }
    }

    /// <summary>
    /// Gets or sets how the triangles blend with what is below them.
    /// </summary>
    public RenderBlend Blend { get; set; } = RenderBlend.PremultipliedOver;

    /// <summary>
    /// Gets the buffer that textures the triangles, or <see langword="null"/> when they draw with vertex colors only.
    /// </summary>
    public IBuffer? SpriteBuffer => _sprite.Buffer;

    /// <summary>
    /// Replaces the sprite buffer. The node holds a lock on the new buffer and releases the lock on the old one.
    /// </summary>
    /// <param name="buffer">The new sprite, or <see langword="null"/> for none.</param>
    public void SetSpriteBuffer(IBuffer? buffer)
    {
        if (ReferenceEquals(buffer, _sprite.Buffer))
        {
            return;
        }

        DropTexture();
        var taken = buffer is null ? default : buffer.Lock();
        _sprite.Dispose();
        _sprite = taken;
        DamageSubtree();
    }

    /// <summary>
    /// Damages the node so that the next frame asks the source for its vertices again.
    /// </summary>
    public void NotifyMeshChanged() => DamageSubtree();

    internal ITexture? GetSpriteTexture(IRenderer renderer)
    {
        if (_sprite.Buffer is not { } sprite)
        {
            return null;
        }

        if (_texture is not null && ReferenceEquals(_textureRenderer, renderer))
        {
            return _texture;
        }

        DropTexture();
        _texture = renderer.ImportTexture(sprite);
        _textureRenderer = _texture is null ? null : renderer;
        return _texture;
    }

    internal override Box SubtreeBounds()
    {
        if (_source is null || _bounds.IsEmpty)
        {
            return default;
        }

        return IsClipped ? _bounds.Intersect(ClipBox) : _bounds;
    }

    internal override void DamageInto(Scene scene, int sceneX, int sceneY)
    {
        var bounds = SubtreeBounds();
        if (!bounds.IsEmpty)
        {
            scene.NotifyDamage(this, bounds.Translated(sceneX, sceneY));
        }
    }

    /// <summary>
    /// Releases the sprite texture and the lock on the sprite buffer.
    /// </summary>
    protected override void OnDestroy()
    {
        DropTexture();
        _sprite.Dispose();
    }

    internal void ReleaseRendererState() => DropTexture();

    private void DropTexture()
    {
        _texture?.Dispose();
        _texture = null;
        _textureRenderer = null;
    }
}
