namespace Basin.Scene;

public sealed class SceneShader : SceneNode
{
    private Box _bounds;
    private IPixelShader? _shader;

    public SceneShader(SceneTree parent)
        : base(parent)
    {
    }

    public IPixelShader? Shader
    {
        get => _shader;
        set
        {
            if (!ReferenceEquals(_shader, value))
            {
                DamageSubtree();
                _shader = value;
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

    public void NotifyShaderChanged() => DamageSubtree();

    internal override Box SubtreeBounds()
    {
        if (_shader is null || _bounds.IsEmpty)
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
}
