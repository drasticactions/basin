namespace Basin.Scene;

/// <summary>
/// A node that fills a rectangle with the output of a pixel shader. It does not draw under a <see cref="SceneTransform"/> with a matrix.
/// </summary>
public sealed class SceneShader : SceneNode
{
    private Box _bounds;
    private IPixelShader? _shader;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneShader"/> class with no shader and empty bounds.
    /// </summary>
    /// <param name="parent">The tree to add the node to.</param>
    public SceneShader(SceneTree parent)
        : base(parent)
    {
    }

    /// <summary>
    /// Gets or sets the shader to run. The node draws nothing while it is <see langword="null"/>.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the rectangle to fill, in local logical pixels.
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
    /// Damages the node so that the next frame runs the shader again, for example after a uniform changes.
    /// </summary>
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
