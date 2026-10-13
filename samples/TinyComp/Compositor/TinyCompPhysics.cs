using System.Diagnostics;
using System.Globalization;
using Basin;
using Basin.Effects;
using Basin.Host;
using Basin.Kinematics;
using Basin.Physics;
using Basin.Scene;
using Basin.Seat;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private const double ShelfPull = 2500;
    private const double SnapSpeed = 400;
    private const double GuideSpeed = 60;
    private const long ThrowSpanNanos = 8_000_000;
    private const int ImpactSquashMillis = 180;
    private const double MaxSquash = 0.12;
    private const double ShakeAmplitude = 14;

    private readonly Dictionary<Workspace, PhysicsSpace> _physicsSpaces = [];
    private readonly Dictionary<IGrabTarget, PhysicsBinding> _physicsBindings = [];
    private readonly Dictionary<ulong, IGrabTarget> _physicsByTag = [];
    private readonly List<IGrabTarget> _physicsScratch = [];
    private readonly List<IGrabTarget> _physicsStale = [];
    private readonly List<Workspace> _physicsSpaceScratch = [];
    private readonly Box[] _wallScratch = new Box[16];
    private Basin.Physics.PhysicsWindows? _physicsWindows;
    private VelocityTracker _throwTracker;
    private bool _physicsFrozen;
    private double? _gravityOverride;
    private int _gravityStep;
    private bool _shakeAttached;
    private bool _physicsRulesStale = true;

    private sealed class PhysicsSpace
    {
        public required PhysicsWorld World { get; init; }

        public required PhysicsWalls Walls { get; init; }

        public required Workspace Workspace { get; init; }

        public Box[] WallBoxes { get; set; } = [];

        public PhysicsShelf?[] Shelves { get; } = new PhysicsShelf?[4];
    }

    private sealed class PhysicsShelf
    {
        public required PhysicsWorld World { get; init; }

        public required PhysicsWalls Walls { get; init; }

        public required OutputView View { get; init; }

        public required CanvasSide Side { get; init; }

        public Box Span { get; set; }
    }

    private sealed class PhysicsBinding
    {
        public required ulong Id { get; init; }

        public PhysicsBody Body { get; set; }

        public PhysicsWorld? World { get; set; }

        public Workspace? Workspace { get; set; }

        public PhysicsShelf? Shelf { get; set; }

        public Rule? Rule { get; set; }

        public int LastX { get; set; }

        public int LastY { get; set; }

        public double Angle { get; set; }

        public bool ReassignAtRest { get; set; }

        public bool Parked { get; set; }

        public bool GridSnap { get; set; }

        public (double X, double Y)? GuideTarget { get; set; }

        public string? GuideLine { get; set; }

        public SpringHandoff Spring;

        public bool Springing { get; set; }

        public double SpringDistance { get; set; }

        public double SpringAngle { get; set; }

        public SquashEffect? Squash { get; set; }
    }

    private bool PhysicsOn => _config.Physics.Enable;

    private IGrabTarget? FastestAwakeBody(OutputView view, out double speed)
    {
        speed = 0.0;
        IGrabTarget? fastest = null;
        foreach (var (window, binding) in _physicsBindings)
        {
            if (!binding.Body.IsAwake || !ReferenceEquals(ViewOfWindow(window), view))
            {
                continue;
            }

            var (vx, vy) = binding.Body.Velocity;
            var bodySpeed = Math.Sqrt((vx * vx) + (vy * vy));
            if (fastest is null || bodySpeed > speed)
            {
                fastest = window;
                speed = bodySpeed;
            }
        }

        return fastest;
    }

    private void ApplyPhysicsSettings()
    {
        _physicsRulesStale = true;
        if (!PhysicsOn)
        {
            ClearPhysics();
            return;
        }

        foreach (var space in _physicsSpaces.Values)
        {
            if (!ReferenceEquals(space.World.Engine, _config.Physics.EngineInstance))
            {
                ClearPhysics();
            }

            break;
        }

        _physicsWindows ??= new Basin.Physics.PhysicsWindows(_toplevels);
        _gravityOverride = null;
        foreach (var space in _physicsSpaces.Values)
        {
            ConfigureWorld(space.World, space.Workspace);
            foreach (var shelf in space.Shelves)
            {
                if (shelf is not null)
                {
                    ConfigureWorld(shelf.World, null);
                    shelf.World.Gravity = ShelfGravity(shelf.Side);
                }
            }
        }

        foreach (var (window, binding) in _physicsBindings)
        {
            if (binding.Body.IsValid)
            {
                binding.Rule = RuleOf(window);
                ApplyBodySettings(window, binding);
            }
        }

        SyncPhysics();
        ScheduleEffectRepaint();
    }

    private void ClearPhysics()
    {
        foreach (var (window, binding) in _physicsBindings)
        {
            var angle = binding.Angle;
            ClearTilt(window, binding);
            TakeTiltFromBody(window, angle);
            if (binding.Squash is { } squash && window.EffectTree is { IsDestroyed: false } tree)
            {
                squash.End(_effects.StackFor(tree));
            }
        }

        _physicsBindings.Clear();
        _physicsByTag.Clear();
        _physicsWindows?.Dispose();
        _physicsWindows = null;
        foreach (var space in _physicsSpaces.Values)
        {
            foreach (var shelf in space.Shelves)
            {
                shelf?.World.Dispose();
            }

            space.World.Dispose();
        }

        _physicsSpaces.Clear();
        DetachShake();
    }

    private void ConfigureWorld(PhysicsWorld world, Workspace? workspace)
    {
        var settings = _config.Physics;
        world.ThrowMultiplier = settings.ThrowMultiplier;
        world.MaxThrowSpeed = settings.MaxThrowSpeed;
        world.StopSpeed = settings.StopSpeed;
        world.ImpactThreshold = settings.ImpactSpeed;
        world.TeleportDistance = Views.Count > 0 ? Views.Max(v => _layout.BoxOf(v.Output).Width) : double.PositiveInfinity;
        world.SpinScale = settings.Rotation ? 0.5 : 0;
        if (workspace is not null)
        {
            world.Gravity = (0, GravityOf(workspace));
        }
    }

    private double GravityOf(Workspace workspace) =>
        _gravityOverride ?? _config.Physics.GravityFor(WorkspaceNumber(workspace));

    private int WorkspaceNumber(Workspace workspace) =>
        ViewOf(workspace) is { } view ? view.Workspaces.IndexOf(workspace) + 1 : 1;

    private static (double X, double Y) ShelfGravity(CanvasSide side) =>
        (side & CanvasSide.Horizontal) != 0 ? (0, -ShelfPull) : (-ShelfPull, 0);

    private Rule? RuleOf(IGrabTarget window) => window switch
    {
        Window w => _config.RuleFor(w.Facts with { Title = w.Toplevel.Title, AppId = w.Toplevel.AppId }),
        XWindow x => x.Rule,
        _ => null,
    };

    private PhysicsMaterial MaterialOf(Rule? rule)
    {
        var settings = _config.Physics;
        return new PhysicsMaterial(
            Density: rule?.Mass ?? settings.Density,
            Friction: rule?.Friction ?? settings.Friction,
            Restitution: rule?.Bounce ?? settings.Restitution,
            GravityScale: rule?.Gravity ?? 1.0,
            LinearDamping: settings.Damping);
    }

    private void ApplyBodySettings(IGrabTarget window, PhysicsBinding binding)
    {
        var body = binding.Body;
        body.Material = MaterialOf(binding.Rule);
        body.CollidesWith = binding.Rule?.NoCollide == true
            ? PhysicsCategory.Edges | PhysicsCategory.Fences
            : PhysicsCategory.All;
        body.Upright = _config.Physics.Upright && !IsHeldTilt(window);
        if (binding.Shelf is { } shelf)
        {
            body.Locks = PhysicsLocks.Rotation |
                ((shelf.Side & CanvasSide.Horizontal) != 0 ? PhysicsLocks.X : PhysicsLocks.Y);
        }
        else
        {
            body.Locks = _config.Physics.Rotation ? PhysicsLocks.None : PhysicsLocks.Rotation;
        }

        if (body.Locks.HasFlag(PhysicsLocks.Rotation) && binding.Angle != 0)
        {
            WriteBack(window, binding, body.Pose);
        }
    }

    private PhysicsSpace SpaceOf(Workspace workspace)
    {
        if (_physicsSpaces.TryGetValue(workspace, out var space))
        {
            return space;
        }

        var world = new PhysicsWorld(_config.Physics.EngineInstance);
        space = new PhysicsSpace { World = world, Walls = new PhysicsWalls(world), Workspace = workspace };
        ConfigureWorld(world, workspace);
        _physicsSpaces[workspace] = space;
        RebuildWalls(space);
        return space;
    }

    private void RebuildWalls(PhysicsSpace space)
    {
        var count = 0;
        foreach (var view in Views)
        {
            if (count == _wallScratch.Length || !_layout.Contains(view.Output))
            {
                continue;
            }

            _wallScratch[count++] = CanvasBoundsOf(view);
        }

        var boxes = _wallScratch.AsSpan(0, count);
        if (boxes.SequenceEqual(space.WallBoxes))
        {
            return;
        }

        space.WallBoxes = boxes.ToArray();
        space.Walls.Rebuild(boxes);
    }

    private Box CanvasBoundsOf(OutputView view)
    {
        var box = _layout.BoxOf(view.Output);
        if (view.Tag is not OutputPolicy || !view.Canvas.Enabled || view.Canvas.IsIdentity)
        {
            return box;
        }

        var canvas = view.Canvas;
        var left = canvas.Left.IsIdentity ? box.X : SpanEdge(canvas.Left);
        var right = canvas.Right.IsIdentity ? box.Right : SpanEdge(canvas.Right);
        var top = canvas.Top.IsIdentity ? box.Y : SpanEdge(canvas.Top);
        var bottom = canvas.Bottom.IsIdentity ? box.Bottom : SpanEdge(canvas.Bottom);
        return new Box(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private (bool Body, PhysicsBodyKind Kind) PhysicsKindOf(IGrabTarget window, Rule? rule)
    {
        if (rule?.Physics == false || window.EffectTree is not { IsDestroyed: false } || IsShelved(window))
        {
            return (false, PhysicsBodyKind.Static);
        }

        Workspace? workspace;
        bool pinned;
        switch (window)
        {
            case Window w:
                if (w.Minimized || w.Workspace is null)
                {
                    return (false, PhysicsBodyKind.Static);
                }

                workspace = w.Workspace;
                pinned = w.Sticky ||
                    w.Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen) ||
                    w.Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized) ||
                    workspace.Tiled.Contains(w);
                break;
            case XWindow x:
                if (x.Minimized || !x.Framable || x.Workspace is null)
                {
                    return (false, PhysicsBodyKind.Static);
                }

                workspace = x.Workspace;
                pinned = x.Maximized || x.XWin.WantsFullscreen;
                break;
            default:
                return (false, PhysicsBodyKind.Static);
        }

        pinned |= rule?.Pin == true;
        pinned |= _mode == DragMode.Resize && ReferenceEquals(_grabWindow, window);
        if (_canvasStates.TryGetValue(window, out var state))
        {
            pinned |= state.ShelfPinned || state.Region == CanvasRegion.Corner || state.Resizing;
        }

        return (true, pinned ? PhysicsBodyKind.Static : PhysicsBodyKind.Dynamic);
    }

    private void SyncPhysics()
    {
        if (!PhysicsOn || _physicsWindows is null)
        {
            return;
        }

        var refreshRules = _physicsRulesStale;
        _physicsRulesStale = false;
        _physicsScratch.Clear();
        _physicsScratch.AddRange(_windows);
        _physicsScratch.AddRange(_xwindows);
        foreach (var window in _physicsScratch)
        {
            _physicsBindings.TryGetValue(window, out var binding);
            var rule = binding is null || refreshRules ? RuleOf(window) : binding.Rule;
            var (wanted, kind) = PhysicsKindOf(window, rule);
            if (!wanted)
            {
                if (binding is not null)
                {
                    Unbind(window, binding);
                }

                continue;
            }

            binding ??= Bind(window, rule);
            if (binding is null)
            {
                continue;
            }

            if (refreshRules)
            {
                binding.Rule = rule;
                ApplyBodySettings(window, binding);
            }

            SyncBinding(window, binding, kind);
        }

        _physicsStale.Clear();
        foreach (var (window, _) in _physicsBindings)
        {
            if (!_physicsScratch.Contains(window))
            {
                _physicsStale.Add(window);
            }
        }

        foreach (var window in _physicsStale)
        {
            ForgetPhysics(window);
        }

        foreach (var space in _physicsSpaces.Values)
        {
            RebuildWalls(space);
            foreach (var shelf in space.Shelves)
            {
                if (shelf is not null)
                {
                    RebuildShelfWalls(shelf);
                }
            }
        }
    }

    private PhysicsBinding? Bind(IGrabTarget window, Rule? rule)
    {
        if (_physicsWindows is null || ToplevelIdOf(SurfaceOf(window)) is not { } id)
        {
            return null;
        }

        var workspace = WorkspaceOfWindow(window)!;
        var shelf = TerraceShelfOf(window, workspace);
        var world = shelf?.World ?? SpaceOf(workspace).World;
        var box = CanvasBoxOf(window);
        var falls = shelf is not null || (GravityOf(workspace) != 0 && rule?.Gravity != 0);
        var body = _physicsWindows.Bind(world, id, box.X, box.Y, PhysicsBodyKind.Dynamic, MaterialOf(rule), awake: falls);
        if (!body.IsValid)
        {
            return null;
        }

        body.Resize(box.Width, box.Height);
        var binding = new PhysicsBinding
        {
            Id = id,
            Body = body,
            World = world,
            Workspace = workspace,
            Shelf = shelf,
            Rule = rule,
            LastX = window.X,
            LastY = window.Y,
        };
        _physicsBindings[window] = binding;
        _physicsByTag[id] = window;
        ApplyBodySettings(window, binding);
        HandTiltToBody(window, binding);
        return binding;
    }

    private void Unbind(IGrabTarget window, PhysicsBinding binding)
    {
        var angle = binding.Angle;
        ClearTilt(window, binding);
        _physicsBindings.Remove(window);
        _physicsByTag.Remove(binding.Id);
        _physicsWindows?.Unbind(binding.Id);
        TakeTiltFromBody(window, angle);
    }

    private void ForgetPhysics(IGrabTarget window)
    {
        if (_physicsBindings.TryGetValue(window, out var binding))
        {
            _physicsBindings.Remove(window);
            _physicsByTag.Remove(binding.Id);
            _physicsWindows?.Unbind(binding.Id);
        }
    }

    private void SyncBinding(IGrabTarget window, PhysicsBinding binding, PhysicsBodyKind kind)
    {
        var workspace = WorkspaceOfWindow(window)!;
        var body = binding.Body;
        if (!body.IsValid)
        {
            return;
        }

        if (!ReferenceEquals(binding.Workspace, workspace))
        {
            MoveBody(window, binding, SpaceOf(workspace).World, null, velocityScale: 0);
            binding.Workspace = workspace;
            body = binding.Body;
        }

        if (!body.IsDragging && binding.Shelf is null && TerraceShelfOf(window, workspace) is { } entered)
        {
            MoveBody(window, binding, entered.World, entered, velocityScale: 1);
            body = binding.Body;
        }
        else if (binding.Shelf is { } shelf && !body.IsDragging && TerraceShelfOf(window, workspace) != shelf)
        {
            MoveBody(window, binding, SpaceOf(workspace).World, null, velocityScale: 1);
            body = binding.Body;
        }

        if (binding.Parked)
        {
            kind = PhysicsBodyKind.Static;
        }

        if (!body.IsDragging && body.Kind != kind)
        {
            body.Kind = kind;
        }

        var box = CanvasBoxOf(window);
        if (box.Width != (int)Math.Round(body.Width) || box.Height != (int)Math.Round(body.Height))
        {
            body.Resize(box.Width, box.Height);
        }

        if (window.X != binding.LastX || window.Y != binding.LastY)
        {
            if (!body.IsDragging)
            {
                body.Teleport(box.X, box.Y, binding.Angle);
            }

            binding.LastX = window.X;
            binding.LastY = window.Y;
        }
    }

    private void MoveBody(IGrabTarget window, PhysicsBinding binding, PhysicsWorld target, PhysicsShelf? shelf, double velocityScale)
    {
        if (binding.World is not { } from || ReferenceEquals(from, target))
        {
            binding.Shelf = shelf;
            return;
        }

        var box = CanvasBoxOf(window);
        var moved = from.MoveTo(binding.Body, target, box.X, box.Y, velocityScale);
        binding.Body = moved;
        binding.World = target;
        binding.Shelf = shelf;
        _physicsWindows?.Rebind(binding.Id, moved);
        ApplyBodySettings(window, binding);
        ScheduleEffectRepaint();
    }

    private static Surface? SurfaceOf(IGrabTarget window) => window switch
    {
        Window w => w.Toplevel.Surface,
        XWindow x => x.XWin.Surface,
        _ => null,
    };

    private static Workspace? WorkspaceOfWindow(IGrabTarget window) => window switch
    {
        Window w => w.Workspace,
        XWindow x => x.Workspace,
        _ => null,
    };

    private PhysicsShelf? TerraceShelfOf(IGrabTarget window, Workspace workspace)
    {
        if (ViewOf(workspace) is not { Tag: OutputPolicy, Canvas: { Enabled: true, Terraces: true } canvas } view ||
            !_canvasStates.TryGetValue(window, out var state) || state.Region != CanvasRegion.Shelf || state.ShelfPinned ||
            state.Dragging)
        {
            return null;
        }

        var box = CanvasBoxOf(window);
        var (side, end) = ZonesHolding(canvas, box);
        var warp = side ?? end;
        if (warp is null)
        {
            return null;
        }

        var which = ReferenceEquals(warp, canvas.Left) ? CanvasSide.Left
            : ReferenceEquals(warp, canvas.Right) ? CanvasSide.Right
            : ReferenceEquals(warp, canvas.Top) ? CanvasSide.Top
            : CanvasSide.Bottom;
        var space = SpaceOf(workspace);
        var index = SideIndex(which);
        if (space.Shelves[index] is not { } shelf)
        {
            var world = new PhysicsWorld(_config.Physics.EngineInstance);
            ConfigureWorld(world, null);
            world.Gravity = ShelfGravity(which);
            shelf = new PhysicsShelf { World = world, Walls = new PhysicsWalls(world), View = view, Side = which };
            space.Shelves[index] = shelf;
            RebuildShelfWalls(shelf);
        }

        return shelf;
    }

    private void RebuildShelfWalls(PhysicsShelf shelf)
    {
        var bounds = CanvasBoundsOf(shelf.View);
        const int Far = 100_000;
        var span = (shelf.Side & CanvasSide.Horizontal) != 0
            ? new Box(bounds.X - Far, bounds.Y, bounds.Width + (2 * Far), bounds.Height)
            : new Box(bounds.X, bounds.Y - Far, bounds.Width, bounds.Height + (2 * Far));
        if (span == shelf.Span)
        {
            return;
        }

        shelf.Span = span;
        shelf.Walls.Rebuild([span]);
    }

    private bool StepPhysics(in FrameTick tick)
    {
        if (!PhysicsOn || _physicsSpaces.Count == 0)
        {
            return StepPhysicsEffects(tick);
        }

        SyncPhysics();
        var running = false;
        _physicsSpaceScratch.Clear();
        _physicsSpaceScratch.AddRange(_physicsSpaces.Keys);
        foreach (var workspace in _physicsSpaceScratch)
        {
            var space = _physicsSpaces[workspace];
            var view = ViewOf(workspace);
            var active = view is not null && view.Active == workspace;
            var frozen = _physicsFrozen || view is null || OverviewOpenOn(view) || view.Canvas.Overview;
            space.World.Frozen = frozen || !active;
            running |= StepWorld(space.World, tick);
            foreach (var shelf in space.Shelves)
            {
                if (shelf is not null)
                {
                    shelf.World.Frozen = frozen || !active;
                    running |= StepWorld(shelf.World, tick);
                }
            }
        }

        running |= StepPhysicsEffects(tick);
        return running;
    }

    private bool StepWorld(PhysicsWorld world, in FrameTick tick)
    {
        StepGuides(world, tick);
        var awake = world.Step(tick);
        var turned = false;
        foreach (var body in world.Moved)
        {
            if (_physicsByTag.TryGetValue(body.Tag, out var window) && _physicsBindings.TryGetValue(window, out var binding))
            {
                turned |= WriteBack(window, binding, body.Pose);
            }
        }

        foreach (var impact in world.Impacts)
        {
            OnImpact(impact, tick);
        }

        if (turned)
        {
            _popupPlacer.PlaceAll();
        }

        SettleBindings(world);
        return awake || world.IsAwake;
    }

    private bool WriteBack(IGrabTarget window, PhysicsBinding binding, in PhysicsPose pose)
    {
        var box = CanvasBoxOf(window);
        var offsetX = box.X - window.X;
        var offsetY = box.Y - window.Y;
        var x = (int)Math.Round(pose.X) - offsetX;
        var y = (int)Math.Round(pose.Y) - offsetY;
        if (TraceEnabled)
        {
            Trace(string.Create(CultureInfo.InvariantCulture, $"physics {NameOf(window)} {pose.X:F2} {pose.Y:F2} {pose.Angle:F4}"));
        }

        if (x != window.X || y != window.Y)
        {
            window.MoveTo(x, y);
        }

        binding.LastX = window.X;
        binding.LastY = window.Y;
        if (pose.Angle == binding.Angle || window.EffectTree is not { IsDestroyed: false } tree)
        {
            return false;
        }

        binding.Angle = pose.Angle;
        _ = PhysicsTransforms.Apply(
            _effects.StackFor(tree),
            new PhysicsPose(0, 0, pose.Angle, offsetX + pose.PivotX, offsetY + pose.PivotY));
        return true;
    }

    private void ClearTilt(IGrabTarget window, PhysicsBinding binding)
    {
        if (binding.Angle == 0 || window.EffectTree is not { IsDestroyed: false } tree)
        {
            return;
        }

        binding.Angle = 0;
        _ = _effects.StackFor(tree).Remove(PhysicsTransforms.Name);
        _popupPlacer.PlaceAll();
    }

    internal bool IsTilted(IGrabTarget window) => AngleOf(window) != 0;

    private RenderTransform PopupTurn(Basin.Shell.Xdg.XdgPopupWindow popup)
    {
        var xdg = popup.Parent;
        while (xdg?.Role is Basin.Shell.Xdg.XdgPopupWindow parent)
        {
            xdg = parent.Parent;
        }

        if (xdg?.Role is not Basin.Shell.Xdg.XdgToplevelWindow toplevel || FindWindow(toplevel) is not { } window ||
            AngleOf(window) is var angle && angle == 0)
        {
            return RenderTransform.Identity;
        }

        var box = CanvasBoxOf(window);
        var geometry = xdg.EffectiveGeometry;
        var pivotX = box.X - window.X + (box.Width / 2.0) - geometry.X;
        var pivotY = box.Y - window.Y + (box.Height / 2.0) - geometry.Y;
        return RenderTransform.RotationAbout(angle, pivotX, pivotY);
    }

    private void SettleBindings(PhysicsWorld world)
    {
        _physicsStale.Clear();
        foreach (var (window, binding) in _physicsBindings)
        {
            if (binding.ReassignAtRest && ReferenceEquals(binding.World, world) && binding.Body.IsValid &&
                !binding.Body.IsAwake && !binding.Springing && binding.GuideTarget is null)
            {
                binding.ReassignAtRest = false;
                _physicsStale.Add(window);
            }
        }

        foreach (var window in _physicsStale)
        {
            ReassignDraggedWorkspace(window);
            ClearCanvasHomeAfterDrop(window);
        }
    }

    private void OnImpact(in PhysicsImpact impact, in FrameTick tick)
    {
        var settings = _config.Physics;
        if (impact.Speed < settings.ImpactSpeed)
        {
            return;
        }

        var squash = Math.Clamp(impact.Speed / 20000.0, 0.02, MaxSquash);
        if (settings.Squash)
        {
            SquashOnImpact(impact.A, impact.NormalX, impact.NormalY, squash, tick);
            SquashOnImpact(impact.B, impact.NormalX, impact.NormalY, squash, tick);
        }

        if (settings.Shake && _post.BeginShake(
                impact.NormalX * ShakeAmplitude * squash / MaxSquash, impact.NormalY * ShakeAmplitude * squash / MaxSquash, tick))
        {
            AttachShake();
        }
    }

    private void SquashOnImpact(PhysicsBody body, double normalX, double normalY, double amount, in FrameTick tick)
    {
        if (!body.IsValid || body.Kind != PhysicsBodyKind.Dynamic || !_physicsByTag.TryGetValue(body.Tag, out var window) ||
            !_physicsBindings.TryGetValue(window, out var binding) || window.EffectTree is not { IsDestroyed: false } tree ||
            binding.Angle != 0)
        {
            return;
        }

        var box = CanvasBoxOf(window);
        var local = new Box(box.X - window.X, box.Y - window.Y, box.Width, box.Height);
        var alongX = Math.Abs(normalX) >= Math.Abs(normalY);
        var width = alongX ? local.Width * (1 - amount) : local.Width * (1 + (amount / 2));
        var height = alongX ? local.Height * (1 + (amount / 2)) : local.Height * (1 - amount);
        var squashed = new Box(
            (int)Math.Round(local.X + ((local.Width - width) / 2)),
            (int)Math.Round(local.Y + ((local.Height - height) / 2)),
            Math.Max(1, (int)Math.Round(width)),
            Math.Max(1, (int)Math.Round(height)));
        var stack = _effects.StackFor(tree);
        binding.Squash ??= new SquashEffect { Fade = false };
        if (binding.Squash.Begin(stack, local, squashed, restoring: true, tick, new AnimationDuration(ImpactSquashMillis)))
        {
            ScheduleEffectRepaint();
        }
    }

    private bool StepPhysicsEffects(in FrameTick tick)
    {
        var running = false;
        foreach (var (window, binding) in _physicsBindings)
        {
            if (binding.Squash is { IsRunning: true } squash && window.EffectTree is { IsDestroyed: false } tree)
            {
                running |= squash.Step(_effects.StackFor(tree), tick);
            }
        }

        if (_shakeAttached && _post.Shake is not { IsRunning: true })
        {
            DetachShake();
        }

        return running;
    }

    private void AttachShake()
    {
        if (_shakeAttached || _post.Shake is not { } shake)
        {
            return;
        }

        foreach (var view in Views)
        {
            view.Scene?.AddPostStage(shake);
        }

        _shakeAttached = true;
        ScheduleEffectRepaint();
    }

    private void DetachShake()
    {
        if (!_shakeAttached || _post.Shake is not { } shake)
        {
            _shakeAttached = false;
            return;
        }

        foreach (var view in Views)
        {
            _ = view.Scene?.RemovePostStage(shake);
        }

        _shakeAttached = false;
        ScheduleEffectRepaint();
    }

    private void BeginPhysicsDrag(IGrabTarget window)
    {
        _throwTracker.Reset();
        _throwTracker.MinSpanNanos = ThrowSpanNanos;
        if (!PhysicsOn)
        {
            return;
        }

        SyncPhysics();
        if (_physicsBindings.TryGetValue(window, out var held) && held.Parked)
        {
            Unpark(window, held);
        }

        if (!_physicsBindings.TryGetValue(window, out var binding) || !binding.Body.IsValid ||
            binding.Body.Kind == PhysicsBodyKind.Static || binding.World is not { Frozen: false } world)
        {
            return;
        }

        CancelGuide(binding);
        binding.GridSnap = false;
        binding.ReassignAtRest = false;
        if (binding.Shelf is not null && WorkspaceOfWindow(window) is { } workspace)
        {
            MoveBody(window, binding, SpaceOf(workspace).World, null, velocityScale: 0);
            world = binding.World!;
        }

        var grabX = window.X + _grabX;
        var grabY = window.Y + _grabY;
        world.BeginDrag(binding.Body, grabX, grabY);
        _throwTracker.Add(grabX, grabY, InputNanos());
        ScheduleEffectRepaint();
    }

    private bool PhysicsDragTo(IGrabTarget window, double canvasX, double canvasY, bool follow)
    {
        if (!_physicsBindings.TryGetValue(window, out var binding) || !binding.Body.IsValid || !binding.Body.IsDragging ||
            binding.World is not { } world)
        {
            return false;
        }

        if (follow)
        {
            canvasX = window.X + _grabX;
            canvasY = window.Y + _grabY;
        }

        _throwTracker.Add(canvasX, canvasY, InputNanos());
        world.Drag(binding.Body, canvasX, canvasY);
        if (!follow)
        {
            _ = WriteBack(window, binding, binding.Body.Pose);
        }

        ScheduleEffectRepaint();
        return !follow;
    }

    private bool EndPhysicsDrag(IGrabTarget window)
    {
        if (!_physicsBindings.TryGetValue(window, out var binding) || !binding.Body.IsValid || !binding.Body.IsDragging ||
            binding.World is not { } world)
        {
            return false;
        }

        var (vx, vy) = _throwTracker.Velocity(InputNanos());
        _throwTracker.Reset();
        world.EndDrag(binding.Body, vx, vy);
        var moving = vx != 0 || vy != 0;
        binding.ReassignAtRest = moving;
        binding.GridSnap = ViewOf(WorkspaceOfWindow(window)!) is { Tag: OutputPolicy, Canvas: { Enabled: true, Settings.DragValue: CanvasDrag.Grid } };
        ScheduleEffectRepaint();
        return moving;
    }

    private long InputNanos() => _lastMotionMillis != 0
        ? (long)_lastMotionMillis * 1_000_000
        : (long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency));

    private uint _lastMotionMillis;

    internal bool GuidedMove(IGrabTarget window, int targetX, int targetY, string line)
    {
        if (!PhysicsOn)
        {
            return false;
        }

        SyncPhysics();
        if (_physicsBindings.TryGetValue(window, out var held) && held.Parked)
        {
            Unpark(window, held);
        }

        if (!_physicsBindings.TryGetValue(window, out var binding) || !binding.Body.IsValid ||
            binding.Body.Kind != PhysicsBodyKind.Dynamic || binding.World is not { Frozen: false } world)
        {
            return false;
        }

        if (binding.Shelf is not null && WorkspaceOfWindow(window) is { } workspace)
        {
            MoveBody(window, binding, SpaceOf(workspace).World, null, velocityScale: 0);
            world = binding.World!;
        }

        CancelGuide(binding);
        var box = CanvasBoxOf(window);
        var toX = box.X + (targetX - window.X);
        var toY = box.Y + (targetY - window.Y);
        var pose = binding.Body.Current;
        var (vx, vy) = Fling.ThrowTo(pose.X, pose.Y, toX, toY, DecayRate(), world.StopSpeed);
        binding.GuideTarget = (toX, toY);
        binding.GuideLine = line;
        binding.GridSnap = false;
        var thrown = binding.Body;
        thrown.Velocity = (vx, vy);
        ScheduleEffectRepaint();
        return true;
    }

    private double DecayRate()
    {
        var damping = _config.Physics.Damping;
        return damping <= 0 ? Fling.DefaultFriction : _config.Physics.EngineInstance.DecayRate(damping);
    }

    private void StepGuides(PhysicsWorld world, in FrameTick tick)
    {
        foreach (var (window, binding) in _physicsBindings)
        {
            if (!ReferenceEquals(binding.World, world) || !binding.Body.IsValid)
            {
                continue;
            }

            var body = binding.Body;
            if (binding.Springing)
            {
                binding.Spring.Step(tick);
                var target = binding.GuideTarget ?? (binding.Spring.TargetX, binding.Spring.TargetY);
                if (!binding.Spring.IsMoving)
                {
                    world.Drag(body, target.X + (body.Width / 2), target.Y + (body.Height / 2), IsHeldTilt(window) ? binding.SpringAngle : 0);
                    world.EndDrag(body, 0, 0);
                    FinishGuide(window, binding);
                    continue;
                }

                var left = Math.Sqrt(
                    ((target.X - binding.Spring.X) * (target.X - binding.Spring.X)) +
                    ((target.Y - binding.Spring.Y) * (target.Y - binding.Spring.Y)));
                var turn = IsHeldTilt(window) ? 1 : binding.SpringDistance > 0 ? Math.Clamp(left / binding.SpringDistance, 0, 1) : 0;
                world.Drag(body, binding.Spring.X + (body.Width / 2), binding.Spring.Y + (body.Height / 2), binding.SpringAngle * turn);
                continue;
            }

            var (vx, vy) = body.Velocity;
            var speed = Math.Sqrt((vx * vx) + (vy * vy));
            if (binding.GuideTarget is { } guide && !body.IsDragging &&
                (!body.IsAwake || speed < Math.Max(GuideSpeed, 2 * world.StopSpeed)))
            {
                var pose = body.Current;
                var distance = Math.Sqrt(((guide.X - pose.X) * (guide.X - pose.X)) + ((guide.Y - pose.Y) * (guide.Y - pose.Y)));
                if (distance < 0.5)
                {
                    body.Teleport(guide.X, guide.Y);
                    FinishGuide(window, binding);
                    continue;
                }

                StartSpring(world, binding, pose, vx, vy, guide.X, guide.Y);
                binding.SpringDistance = distance;
            }
            else if (binding.GridSnap && !body.IsDragging && body.Kind == PhysicsBodyKind.Dynamic && speed < SnapSpeed &&
                ViewOf(WorkspaceOfWindow(window)!) is { Tag: OutputPolicy } view)
            {
                binding.GridSnap = false;
                var pose = body.Current;
                var fling = new Fling(DecayRate(), world.StopSpeed);
                fling.Start(pose.X, pose.Y, vx, vy);
                var targets = new SnapTargets();
                var cell = view.Canvas.Settings.GridCellSize;
                targets.Add(SnapTarget.Grid(0, 0, cell, cell));
                var spring = new SpringHandoff(SpringStiffness, 1.0);
                if (targets.Handoff(fling, ref spring))
                {
                    var (toX, toY) = (spring.TargetX, spring.TargetY);
                    if (WallsOf(binding) is { Bounds.IsEmpty: false } walls)
                    {
                        toX = FitCell(toX, walls.Bounds.X, walls.Bounds.Right, body.Width, cell);
                        toY = FitCell(toY, walls.Bounds.Y, walls.Bounds.Bottom, body.Height, cell);
                    }

                    binding.GuideTarget = (toX, toY);
                    binding.GuideLine = $"SNAP {NameOf(window)} to={(int)toX} y={(int)toY}";
                    StartSpring(world, binding, pose, vx, vy, toX, toY);
                    binding.SpringDistance = Math.Sqrt(((toX - pose.X) * (toX - pose.X)) + ((toY - pose.Y) * (toY - pose.Y)));
                }
            }
        }
    }

    private const double SpringStiffness = 200;

    private PhysicsWalls? WallsOf(PhysicsBinding binding) =>
        binding.Shelf?.Walls ?? (binding.Workspace is { } workspace && _physicsSpaces.TryGetValue(workspace, out var space) ? space.Walls : null);

    private static double FitCell(double target, int low, int high, double size, int cell)
    {
        var first = Math.Ceiling(low / (double)cell) * cell;
        var last = Math.Floor((high - size) / cell) * cell;
        return last < first ? target : Math.Clamp(target, first, last);
    }

    private static void StartSpring(PhysicsWorld world, PhysicsBinding binding, in PhysicsPose pose, double vx, double vy, double toX, double toY)
    {
        var body = binding.Body;
        world.BeginDrag(body, pose.X + pose.PivotX, pose.Y + pose.PivotY);
        binding.Spring = new SpringHandoff(SpringStiffness, 1.0) { Epsilon = 0.25 };
        binding.Spring.Start(pose.X, pose.Y, vx, vy, toX, toY);
        binding.Springing = true;
        binding.SpringAngle = pose.Angle;
    }

    private void FinishGuide(IGrabTarget window, PhysicsBinding binding)
    {
        binding.Springing = false;
        var line = binding.GuideLine;
        var distance = binding.SpringDistance;
        binding.GuideTarget = null;
        binding.GuideLine = null;
        binding.SpringDistance = 0;
        binding.Parked = line?.StartsWith("PARK ", StringComparison.Ordinal) == true;
        if (binding.Parked && binding.Body.IsValid)
        {
            var parked = binding.Body;
            parked.Kind = PhysicsBodyKind.Static;
        }

        if (line is not null)
        {
            _report.Line(string.Create(CultureInfo.InvariantCulture, $"{line} guided={distance:F1}"));
        }

        ScheduleEffectRepaint();
    }

    private void Unpark(IGrabTarget window, PhysicsBinding binding)
    {
        binding.Parked = false;
        var (_, kind) = PhysicsKindOf(window, binding.Rule);
        var body = binding.Body;
        if (body.IsValid)
        {
            body.Kind = kind;
        }
    }

    private static void CancelGuide(PhysicsBinding binding)
    {
        if (binding.Springing && binding.World is { } world && binding.Body.IsValid)
        {
            world.EndDrag(binding.Body, 0, 0);
        }

        binding.Springing = false;
        binding.GuideTarget = null;
        binding.GuideLine = null;
        binding.SpringDistance = 0;
    }

    internal void CycleGravity()
    {
        var steps = _config.Physics.GravitySteps;
        _gravityStep = (_gravityStep + 1) % Math.Max(1, steps.Length);
        SetGravity(steps.Length == 0 ? 0 : steps[_gravityStep]);
    }

    internal void SetGravity(double? gravity)
    {
        _gravityOverride = gravity;
        foreach (var space in _physicsSpaces.Values)
        {
            space.World.Gravity = (0, GravityOf(space.Workspace));
        }

        _report.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"GRAVITY {(gravity is { } g ? g.ToString("F0", CultureInfo.InvariantCulture) : "config")}"));
        ScheduleEffectRepaint();
    }

    internal void SetPhysicsFrozen(bool frozen)
    {
        _physicsFrozen = frozen;
        _report.Line($"PHYSICS frozen={(frozen ? "yes" : "no")}");
        ScheduleEffectRepaint();
    }

    internal bool ThrowWindow(IGrabTarget window, double vx, double vy, double spin = 0)
    {
        if (!PhysicsOn)
        {
            _report.Line("THROW refused: physics off");
            return false;
        }

        SyncPhysics();
        if (_physicsBindings.TryGetValue(window, out var held) && held.Parked)
        {
            Unpark(window, held);
        }

        if (!_physicsBindings.TryGetValue(window, out var binding) || !binding.Body.IsValid ||
            binding.Body.Kind != PhysicsBodyKind.Dynamic)
        {
            _report.Line($"THROW {NameOf(window)} refused: not a dynamic body");
            return false;
        }

        CancelGuide(binding);
        if (binding.Shelf is not null && WorkspaceOfWindow(window) is { } workspace)
        {
            MoveBody(window, binding, SpaceOf(workspace).World, null, velocityScale: 0);
        }

        var thrown = binding.Body;
        thrown.Velocity = (vx, vy);
        if (spin != 0 && !thrown.AngularLocked)
        {
            thrown.AngularVelocity = spin;
        }

        binding.ReassignAtRest = true;
        _report.Line(string.Create(CultureInfo.InvariantCulture, $"THROW {NameOf(window)} vx={vx:F0} vy={vy:F0}"));
        ScheduleEffectRepaint();
        return true;
    }

    internal string PhysicsWhere(IGrabTarget window)
    {
        if (!_physicsBindings.TryGetValue(window, out var binding) || !binding.Body.IsValid)
        {
            return "body=none";
        }

        var body = binding.Body;
        var (vx, vy) = body.Velocity;
        var world = binding.Shelf is { } shelf ? $"shelf-{ZoneName(shelf.Side)}" : "desktop";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"body={KindName(body.Kind)} world={world} awake={(body.IsAwake ? "yes" : "no")} angle={body.Pose.Angle:F4} vx={vx:F0} vy={vy:F0} w={body.Width:F0} h={body.Height:F0}");
    }

    private static string KindName(PhysicsBodyKind kind) => kind switch
    {
        PhysicsBodyKind.Dynamic => "dynamic",
        PhysicsBodyKind.Kinematic => "kinematic",
        _ => "static",
    };

    internal void WritePhysicsState(System.Text.Json.Utf8JsonWriter json)
    {
        json.WriteStartObject();
        json.WriteBoolean("enabled", PhysicsOn);
        json.WriteString("engine", _config.Physics.Engine);
        json.WriteBoolean("frozen", _physicsFrozen);
        if (_gravityOverride is { } gravity)
        {
            json.WriteNumber("gravity", gravity);
        }
        else
        {
            json.WriteNull("gravity");
        }

        var awake = false;
        foreach (var space in _physicsSpaces.Values)
        {
            awake |= space.World.IsAwake;
            foreach (var shelf in space.Shelves)
            {
                awake |= shelf?.World.IsAwake == true;
            }
        }

        json.WriteBoolean("awake", awake);
        json.WriteStartArray("bodies");
        foreach (var (window, binding) in _physicsBindings)
        {
            if (!binding.Body.IsValid)
            {
                continue;
            }

            var body = binding.Body;
            var pose = body.Pose;
            var (vx, vy) = body.Velocity;
            json.WriteStartObject();
            json.WriteString("window", NameOf(window));
            json.WriteNumber("toplevel", binding.Id);
            json.WriteString("kind", KindName(body.Kind));
            json.WriteString("world", binding.Shelf is { } shelf ? "shelf-" + ZoneName(shelf.Side) : "desktop");
            json.WriteNumber("x", window.X);
            json.WriteNumber("y", window.Y);
            json.WriteNumber("width", body.Width);
            json.WriteNumber("height", body.Height);
            json.WriteNumber("angle", pose.Angle);
            json.WriteNumber("vx", vx);
            json.WriteNumber("vy", vy);
            json.WriteBoolean("awake", body.IsAwake);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }
}
