using Basin.Scene;

namespace Basin.Effects;

public sealed class SwitcherEffect
{
    private struct Pose
    {
        public double X;
        public double Y;
        public double Scale;
        public double Yaw;
        public float Alpha;
    }

    private struct Card
    {
        public TransformStack Stack;
        public SceneTransform? Node;
        public SceneTree Root;
        public Box Bounds;
        public Pose From;
        public Pose To;
        public Pose Applied;
    }

    private readonly double _maxYaw;
    private readonly long _flyNanos;
    private readonly long _retargetNanos;
    private Card[] _cards = [];
    private int _count;
    private int _selected;
    private bool _ending;
    private Box _area;
    private double _camera;
    private EffectTimeline _timeline;

    public SwitcherEffect(double maxYawRadians = 0.96, long flyNanos = 260_000_000, long retargetNanos = 170_000_000)
    {
        _maxYaw = maxYawRadians;
        _flyNanos = flyNanos;
        _retargetNanos = retargetNanos;
        _timeline.Easing = EasingCurve.Sigmoid;
    }

    public bool IsActive => _count > 0;

    public bool IsDismissing => _ending;

    public int Selected => _selected;

    public void Begin(IReadOnlyList<TransformStack> stacks, in Box area, int selected)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        RemoveAll();
        _area = area;
        _camera = Math.Max(area.Width, 400);
        _count = stacks.Count;
        _selected = Math.Clamp(selected, 0, Math.Max(0, _count - 1));
        if (_cards.Length < _count)
        {
            _cards = new Card[_count];
        }

        for (var i = 0; i < _count; i++)
        {
            var stack = stacks[i];
            var node = stack.Get("switcher") ?? stack.Add(TransformStack.ZOrder.Transform3D, "switcher");
            var card = new Card { Stack = stack, Root = stack.Root, Bounds = node.ContentBounds };
            if (card.Bounds.Width > 0 && card.Bounds.Height > 0)
            {
                card.Node = node;
                card.From = NaturalPose(in card);
                card.Applied = card.From;
            }

            _cards[i] = card;
        }

        Retarget();
        _timeline.Start(_flyNanos);
    }

    public void Select(int index)
    {
        if (_count == 0 || _ending)
        {
            return;
        }

        _selected = Math.Clamp(index, 0, _count - 1);
        for (var i = 0; i < _count; i++)
        {
            _cards[i].From = _cards[i].Applied;
        }

        Retarget();
        _timeline.Start(_retargetNanos);
    }

    public void End()
    {
        if (_count == 0 || _ending)
        {
            return;
        }

        _ending = true;
        for (var i = 0; i < _count; i++)
        {
            ref var card = ref _cards[i];
            card.From = card.Applied;
            card.To = NaturalPose(in card);
        }

        _timeline.Start(_flyNanos);
    }

    public bool Step(in FrameTick tick)
    {
        if (_count == 0)
        {
            return false;
        }

        var t = _timeline.Progress(tick);
        var running = _timeline.Running(tick);
        for (var i = 0; i < _count; i++)
        {
            ref var card = ref _cards[i];
            if (card.Node is not { IsDestroyed: false } node || card.Root.IsDestroyed)
            {
                continue;
            }

            var pose = Lerp(in card.From, in card.To, t);
            card.Applied = pose;
            node.Matrix = Projection.Card(
                card.Bounds, pose.X - card.Root.X, pose.Y - card.Root.Y, pose.Scale, pose.Yaw, _camera);
            node.Alpha = pose.Alpha;
        }

        if (!running && _ending)
        {
            RemoveAll();
            return false;
        }

        return true;
    }

    private void Retarget()
    {
        for (var i = 0; i < _count; i++)
        {
            ref var card = ref _cards[i];
            if (card.Node is not null)
            {
                card.To = LayoutPose(i, in card);
            }
        }
    }

    private static Pose NaturalPose(in Card card) => new()
    {
        X = card.Root.X + card.Bounds.X + (card.Bounds.Width / 2.0),
        Y = card.Root.Y + card.Bounds.Y + (card.Bounds.Height / 2.0),
        Scale = 1,
        Yaw = 0,
        Alpha = 1,
    };

    private Pose LayoutPose(int index, in Card card)
    {
        var k = index - _selected;
        var cx = _area.X + (_area.Width / 2.0);
        var cy = _area.Y + (_area.Height / 2.0);
        var scale = Math.Min(_area.Height * 0.42 / card.Bounds.Height, _area.Width * 0.42 / card.Bounds.Width);
        if (k == 0)
        {
            return new Pose { X = cx, Y = cy, Scale = scale, Yaw = 0, Alpha = 1 };
        }

        var side = Math.Sign(k);
        return new Pose
        {
            X = cx + (side * _area.Width * 0.14) + (k * _area.Width * 0.11),
            Y = cy,
            Scale = scale * 0.7,
            Yaw = -side * _maxYaw,
            Alpha = (float)Math.Max(0.55, 1.0 - (0.15 * Math.Abs(k))),
        };
    }

    private static Pose Lerp(in Pose a, in Pose b, double t) => new()
    {
        X = a.X + ((b.X - a.X) * t),
        Y = a.Y + ((b.Y - a.Y) * t),
        Scale = a.Scale + ((b.Scale - a.Scale) * t),
        Yaw = a.Yaw + ((b.Yaw - a.Yaw) * t),
        Alpha = (float)(a.Alpha + ((b.Alpha - a.Alpha) * t)),
    };

    private void RemoveAll()
    {
        for (var i = 0; i < _count; i++)
        {
            _cards[i].Stack.Remove("switcher");
            _cards[i] = default;
        }

        _count = 0;
        _ending = false;
    }
}
