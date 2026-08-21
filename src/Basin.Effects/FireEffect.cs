using Basin.Scene;

namespace Basin.Effects;

public sealed class FireEffect : IMeshSource
{
    private const int SpriteSize = 16;
    private const float Slowdown = 0.8f;

    private readonly FireOptions _options;
    private float[] _fade = [];
    private float[] _life = [];
    private float[] _x = [];
    private float[] _y = [];
    private float[] _speedX = [];
    private float[] _speedY = [];
    private float[] _startX = [];
    private float[] _baseRadius = [];
    private float[] _colorR = [];
    private float[] _colorG = [];
    private float[] _colorB = [];
    private int _count;

    private ulong _rng;
    private int _alive;
    private EffectTimeline _timeline;
    private bool _hiding;
    private long _lastNanos;
    private bool _hasLast;

    private SceneTransform? _node;
    private SceneTree? _wrapper;
    private SceneMesh? _flame;
    private SceneMesh? _scorch;
    private SceneShader? _shaderNode;
    private long _shaderStart;
    private MemoryBuffer? _sprite;
    private Box _contentBounds;
    private readonly DarkSource _darkSource;

    public IPixelShader? Shader { get; set; }

    public FireEffect(FireOptions options = default)
    {
        _options = options == default ? new FireOptions() : options;
        _rng = _options.Seed;
        _darkSource = new DarkSource(this);
    }

    public bool IsRunning => _node is { IsDestroyed: false };

    public void Begin(TransformStack stack, bool hiding, in FrameTick now, long durationNanos)
    {
        Begin(stack, hiding, durationNanos);
        _timeline.Anchor(now);
    }

    public void Begin(TransformStack stack, bool hiding, long durationNanos)
    {
        End();
        _hiding = hiding;
        _hasLast = false;
        _alive = 0;

        var node = stack.Add(TransformStack.ZOrder.Effect + 1, "fire");
        _node = node;
        _contentBounds = node.ContentBounds;

        if (Shader is not null)
        {
            var heightScaleForShader = Math.Min(_contentBounds.Height / 400.0, 3.0);
            _timeline.Easing = EasingCurve.Linear;
            _timeline.Start((long)(durationNanos * Math.Max(heightScaleForShader, 0.25)));
            _wrapper = WrapChildren(node);
            _shaderNode = new SceneShader(node) { Shader = Shader };
            _shaderStart = -1;
            UpdateShape(_hiding ? 1.0 : 0.0);
            return;
        }

        var widthScale = Math.Min(_contentBounds.Width / 400.0, 3.5);
        _count = Math.Clamp((int)(_options.ParticleCount * Math.Max(widthScale, 0.05)), 1, 65536);
        if (_life.Length < _count)
        {
            _fade = new float[_count];
            _life = new float[_count];
            _x = new float[_count];
            _y = new float[_count];
            _speedX = new float[_count];
            _speedY = new float[_count];
            _startX = new float[_count];
            _baseRadius = new float[_count];
            _colorR = new float[_count];
            _colorG = new float[_count];
            _colorB = new float[_count];
        }

        Array.Clear(_life, 0, _count);
        var heightScale = Math.Min(_contentBounds.Height / 400.0, 3.0);
        _timeline.Easing = EasingCurve.Linear;
        _timeline.Start((long)(durationNanos * Math.Max(heightScale, 0.25)));

        _wrapper = WrapChildren(node);

        _scorch = new SceneMesh(node)
        {
            Blend = RenderBlend.PremultipliedOver,
            Source = _darkSource,
        };
        _flame = new SceneMesh(node)
        {
            Blend = RenderBlend.Additive,
            Source = this,
        };
        _scorch.SetSpriteBuffer(Sprite());
        _flame.SetSpriteBuffer(_sprite);
        UpdateShape(_hiding ? 1.0 : 0.0);
    }

    private static SceneTree WrapChildren(SceneTransform node)
    {
        var wrapper = new SceneTree(node);
        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            if (!ReferenceEquals(child, wrapper))
            {
                child.Reparent(wrapper);
            }
        }

        wrapper.Children.Reverse();
        return wrapper;
    }

    public bool Step(TransformStack stack, in FrameTick tick)
    {
        if (_node is not { IsDestroyed: false } || (_flame is null && _shaderNode is null))
        {
            return false;
        }

        var progress = _timeline.Progress(tick);
        var reveal = _hiding ? 1.0 - progress : progress;

        if (_shaderNode is { IsDestroyed: false } shaderNode && Shader is { } shader)
        {
            if (_shaderStart < 0)
            {
                _shaderStart = tick.TargetPresentNanos;
            }

            var bounds = shaderNode.Bounds;
            var pad = (float)_options.Padding;
            Span<PixelShaderUniformValue> values =
            [
                (float)((tick.TargetPresentNanos - _shaderStart) / 1e9),
                (float)reveal,
                (_options.Seed % 1024) / 64f,
                (pad / bounds.Width, pad / bounds.Height, (float)_contentBounds.Width / bounds.Width, (float)_contentBounds.Height / bounds.Height),
                (_options.Color.R, _options.Color.G, _options.Color.B),
            ];
            shader.SetUniforms(values);
            shaderNode.NotifyShaderChanged();
            UpdateShape(reveal);
            if (_timeline.Running(tick))
            {
                return true;
            }

            End(stack);
            return false;
        }

        var dt = 1f;
        if (_hasLast)
        {
            dt = (float)((tick.TargetPresentNanos - _lastNanos) / 16_666_667.0);
        }

        _lastNanos = tick.TargetPresentNanos;
        _hasLast = true;

        if (_timeline.Running(tick))
        {
            Spawn(reveal, _count / 10);
        }

        Update(Math.Clamp(dt, 0f, 4f));
        UpdateShape(reveal);
        _flame!.NotifyMeshChanged();
        _scorch?.NotifyMeshChanged();

        if (_timeline.Running(tick) || _alive > 0)
        {
            return true;
        }

        End(stack);
        return false;
    }

    public void End(TransformStack? stack = null)
    {
        if (_node is { IsDestroyed: false } node)
        {
            _flame?.Destroy();
            _scorch?.Destroy();
            _shaderNode?.Destroy();
            if (_wrapper is { IsDestroyed: false } wrapper)
            {
                var moved = wrapper.Children.Count;
                for (var i = moved - 1; i >= 0; i--)
                {
                    wrapper.Children[i].Reparent(node);
                }

                node.Children.Reverse(node.Children.Count - moved, moved);
                wrapper.Destroy();
            }

            stack?.Remove("fire");
        }

        _sprite?.Destroy();
        _sprite = null;
        _flame = null;
        _scorch = null;
        _shaderNode = null;
        _wrapper = null;
        _node = null;
    }

    public int VertexCount(in Box bounds) => _alive * 6;

    public void WriteVertices(in Box bounds, Span<MeshVertex> into) => WriteQuads(into, dark: false);

    private void WriteQuads(Span<MeshVertex> into, bool dark)
    {
        var write = 0;
        for (var i = 0; i < _count && write + 6 <= into.Length; i++)
        {
            var life = _life[i];
            if (life <= 0)
            {
                continue;
            }

            var radius = _baseRadius[i] * MathF.Sqrt(life);
            var alpha = life;
            var tint = dark
                ? new RenderColor(0f, 0f, 0f, 0.5f * alpha)
                : new RenderColor(_colorR[i] * alpha, _colorG[i] * alpha, _colorB[i] * alpha, alpha);
            var x = _x[i];
            var y = _y[i];
            into[write++] = new MeshVertex(x - radius, y - radius, 0, 0, tint);
            into[write++] = new MeshVertex(x + radius, y - radius, SpriteSize, 0, tint);
            into[write++] = new MeshVertex(x - radius, y + radius, 0, SpriteSize, tint);
            into[write++] = new MeshVertex(x + radius, y - radius, SpriteSize, 0, tint);
            into[write++] = new MeshVertex(x + radius, y + radius, SpriteSize, SpriteSize, tint);
            into[write++] = new MeshVertex(x - radius, y + radius, 0, SpriteSize, tint);
        }

        while (write + 6 <= into.Length)
        {
            into[write++] = default;
        }
    }

    private sealed class DarkSource(FireEffect owner) : IMeshSource
    {
        public int VertexCount(in Box bounds) => owner._alive * 6;

        public void WriteVertices(in Box bounds, Span<MeshVertex> into) => owner.WriteQuads(into, dark: true);
    }

    private void UpdateShape(double reveal)
    {
        if (_wrapper is { IsDestroyed: false } wrapper)
        {
            var height = Math.Max(1, (int)Math.Round(_contentBounds.Height * reveal));
            wrapper.ClipBox = new Box(_contentBounds.X, _contentBounds.Y, Math.Max(1, _contentBounds.Width), height);
        }

        var pad = _options.Padding;
        var bounds = new Box(
            _contentBounds.X - pad,
            _contentBounds.Y - pad,
            _contentBounds.Width + (2 * pad),
            _contentBounds.Height + (2 * pad));
        if (_flame is { IsDestroyed: false } flame)
        {
            flame.Bounds = bounds;
        }

        if (_scorch is { IsDestroyed: false } scorch)
        {
            scorch.Bounds = bounds;
        }

        if (_shaderNode is { IsDestroyed: false } shaderNode)
        {
            shaderNode.Bounds = bounds;
        }
    }

    private void Spawn(double reveal, int toSpawn)
    {
        var line = (float)(_contentBounds.Y + (_contentBounds.Height * reveal));
        for (var i = 0; i < _count && toSpawn > 0; i++)
        {
            if (_life[i] > 0)
            {
                continue;
            }

            toSpawn--;
            _life[i] = 1f;
            _fade[i] = 0.1f + (0.5f * NextFloat());
            _x[i] = _contentBounds.X + (NextFloat() * _contentBounds.Width);
            _y[i] = line - 10f + (20f * NextFloat());
            _startX[i] = _x[i];
            _speedX[i] = -10f + (20f * NextFloat());
            _speedY[i] = -25f + (30f * NextFloat());
            _baseRadius[i] = _options.ParticleSize * (0.8f + (0.4f * NextFloat()));
            if (_options.RandomColor)
            {
                _colorR[i] = 2f * MathF.Pow(NextFloat(), 16f);
                _colorG[i] = 2f * MathF.Pow(NextFloat(), 16f);
                _colorB[i] = 2f * MathF.Pow(NextFloat(), 16f);
            }
            else
            {
                _colorR[i] = Vary(_options.Color.R);
                _colorG[i] = Vary(_options.Color.G);
                _colorB[i] = Vary(_options.Color.B);
            }

            _alive++;
        }
    }

    private float Vary(float channel)
    {
        var amount = channel * 0.857f / 2f;
        var low = channel - amount;
        var high = Math.Min(channel + amount, 1f);
        return low + ((high - low) * NextFloat());
    }

    private void Update(float dt)
    {
        var move = 0.2f * Slowdown * dt;
        var accelerate = 0.3f * Slowdown * dt;
        for (var i = 0; i < _count; i++)
        {
            if (_life[i] <= 0)
            {
                continue;
            }

            _x[i] += _speedX[i] * move;
            _y[i] += _speedY[i] * move;
            _speedX[i] += (_startX[i] < _x[i] ? -1f : 1f) * accelerate;
            _speedY[i] += -3f * accelerate;
            _life[i] -= _fade[i] * accelerate;
            if (_life[i] <= 0)
            {
                _alive--;
            }
        }
    }

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / (float)(1 << 24);
    }

    private unsafe MemoryBuffer Sprite()
    {
        _sprite?.Destroy();
        var sprite = new MemoryBuffer(SpriteSize, SpriteSize, DrmFormat.Argb8888);
        if (sprite.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            try
            {
                for (var y = 0; y < SpriteSize; y++)
                {
                    var row = (uint*)(view.Data + (y * view.Stride));
                    for (var x = 0; x < SpriteSize; x++)
                    {
                        var dx = x + 0.5 - (SpriteSize / 2.0);
                        var dy = y + 0.5 - (SpriteSize / 2.0);
                        var falloff = Math.Clamp(1.0 - (Math.Sqrt((dx * dx) + (dy * dy)) / (SpriteSize / 2.0)), 0, 1);
                        var value = (uint)((Math.Pow(falloff, 0.6) * 255.0) + 0.5);
                        row[x] = (value << 24) | (value << 16) | (value << 8) | value;
                    }
                }
            }
            finally
            {
                sprite.EndDataAccess();
            }
        }

        _sprite = sprite;
        return sprite;
    }
}
