namespace Basin.Scene;

public sealed class SceneMesh : SceneNode
{
    private Box _bounds;
    private IMeshSource? _source;
    private BufferLock _sprite;
    private ITexture? _texture;
    private IRenderer? _textureRenderer;

    public SceneMesh(SceneTree parent)
        : base(parent)
    {
    }

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

    public RenderBlend Blend { get; set; } = RenderBlend.PremultipliedOver;

    public IBuffer? SpriteBuffer => _sprite.Buffer;

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

    protected override void OnDestroy()
    {
        DropTexture();
        _sprite.Dispose();
    }

    private void DropTexture()
    {
        _texture?.Dispose();
        _texture = null;
        _textureRenderer = null;
    }
}
