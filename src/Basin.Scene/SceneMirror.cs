using Pixman;

namespace Basin.Scene;

public sealed class SceneMirror : SceneNode
{
    private SceneTree? _source;
    private Scene? _registered;
    private int _width;
    private int _height;
    private int _sourceX;
    private int _sourceY;

    public SceneMirror(SceneTree parent, SceneTree source, int width, int height)
        : base(parent)
    {
        _width = width;
        _height = height;
        Source = source;
    }

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

    public int SourceX
    {
        get => _sourceX;
        set => Resize(ref _sourceX, value);
    }

    public int SourceY
    {
        get => _sourceY;
        set => Resize(ref _sourceY, value);
    }

    public int Width
    {
        get => _width;
        set => Resize(ref _width, value);
    }

    public int Height
    {
        get => _height;
        set => Resize(ref _height, value);
    }

    public Box Box => new(0, 0, _width, _height);

    public bool HasSource => _source is { IsDestroyed: false };

    protected override (int Width, int Height) ContentSize => (_width, _height);

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
