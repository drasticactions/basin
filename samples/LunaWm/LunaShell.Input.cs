using Basin;
using Basin.Config;

namespace LunaWm;

internal sealed partial class LunaShell
{
    internal const double TapSlop = 10;
    internal const int HoldMillis = 500;
    internal const double HoldSlop = 12;
    internal const double CloseVelocity = CardFlick.CloseVelocity;

    private readonly HashSet<uint> _consumedKeys = [];
    private CardDrag? _drag;
    private EdgeState _edge;
    private Card? _pinchCard;
    private LunaView? _pinchView;
    private bool _pinchFromMaximized;
    private double _pinchProgress;
    private double _padX;
    private double _padY;
    private uint _padFingers;
    private LunaView? _padView;
    private double _padPinchScale = 1;
    private double _axisAccum;

    private struct EdgeState
    {
        public LunaView? View;
        public LunaEdge Edge;
        public EdgeAxis Axis;
        public double X0;
        public double Y0;
        public double X;
        public double Y;
        public uint StartTime;
        public bool QuickLaunch;
        public IEventSource? HoldTimer;
        public double RestX;
        public double RestY;
        public bool RaisesKeyboard;
    }

    internal bool HandleKey(uint timeMs, uint key, uint keysym, Modifiers held, bool pressed, bool bareSuper)
    {
        if (!pressed && _consumedKeys.Remove(key))
        {
            return true;
        }

        if (_locked)
        {
            return LockKey(timeMs, key, keysym, held, pressed);
        }

        var view = PlacementView();
        if (bareSuper && _config.Bindings.BareSuper is { } bare)
        {
            RunAction(view, bare);
            return true;
        }

        if (!pressed)
        {
            return false;
        }

        var fullscreen = !view.InCardView && view.Deck.ActiveCard is { Fullscreen: true };
        if (_config.Bindings.Match(keysym, held, fullscreen) is { Action: { } action })
        {
            _consumedKeys.Add(key);
            RunAction(view, action);
            return true;
        }

        if (JustTypeKey(view, key, keysym, held))
        {
            _consumedKeys.Add(key);
            return true;
        }

        return false;
    }

    internal void RunAction(LunaView view, string action)
    {
        _report.Line($"ACTION {action}");
        switch (action)
        {
            case "card_view":
                if (LauncherOpen(view))
                {
                    ShowLauncher(view, false);
                }
                else
                {
                    ToggleCardView(view);
                }

                break;
            case "launcher":
                ShowLauncher(view, !LauncherOpen(view));
                break;
            case "quick_launch":
                ShowQuickLaunch(view, !QuickLaunchOpen(view));
                break;
            case "maximize":
                if (view.InCardView)
                {
                    Maximize(view);
                }

                break;
            case "back":
                Back(view);
                break;
            case "next":
                SwitchCard(view, 1);
                break;
            case "previous":
                SwitchCard(view, -1);
                break;
            case "home":
                Home(view);
                break;
            case "dashboard":
                ShowDashboard(view, !DashboardOpen(view));
                break;
            case "lock":
                Lock();
                break;
            case "keyboard":
                ToggleKeyboard();
                break;
            case "inspector":
                ToggleInspector();
                break;
            case "close":
                if (Focused is { } focused)
                {
                    CloseCard(focused.Parent is null ? focused : focused);
                }
                else if (view.InCardView && view.Deck.ActiveCard is { } active)
                {
                    FlickClose(view, active, 0);
                }

                break;
            default:
                _access?.Run(action);
                break;
        }
    }

    private void Home(LunaView view)
    {
        if (LauncherOpen(view))
        {
            ShowLauncher(view, false);
            return;
        }

        if (view.InCardView || view.Deck.IsEmpty)
        {
            ShowLauncher(view, true);
            return;
        }

        EnterCardView(view);
    }

    internal void Back(LunaView view)
    {
        if (CloseTopShellUi(view))
        {
            _report.Line("BACK shell");
            return;
        }

        if (!view.InCardView && Focused is { } focused)
        {
            _seat.SendKey(Keysym.FromName(_config.BackKey));
            _report.Line($"BACK {focused.Id} {_config.BackKey}");
            return;
        }

        _report.Line("BACK none");
    }

    internal string? ShellCursorAt(double x, double y)
    {
        var view = ViewAt(x, y);
        return view.InCardView && !view.Deck.IsEmpty && !_locked ? "default" : null;
    }

    internal bool ShellPress(int id, double x, double y, uint timeMs, uint button)
    {
        if (_locked || _drag is not null)
        {
            return false;
        }

        var view = ViewAt(x, y);
        if (DismissOnPress(view, x - view.Box.X, y - view.Box.Y))
        {
            return true;
        }

        if (!view.InCardView || button != InputCodes.BtnLeft)
        {
            return false;
        }

        var localX = x - view.Box.X;
        var localY = y - view.Box.Y;
        var card = CardAt(view, localX, localY);
        var drag = new CardDrag
        {
            View = view,
            Id = id,
            Card = card,
            StartX = localX,
            StartY = localY,
            StartTime = timeMs,
            X = localX,
            Y = localY,
            StartScroll = view.Scroll,
        };
        drag.Tracker.Add(localX, localY, 0);
        if (card is not null)
        {
            drag.StartPose = card.Current;
            drag.HoldTimer = _host.Loop.AddTimer(() => BeginReorder(drag));
            drag.HoldTimer.UpdateTimer(HoldMillis);
        }

        _drag = drag;
        return true;
    }

    internal void ShellMove(int id, double x, double y, uint timeMs)
    {
        if (_drag is not { } drag || drag.Id != id)
        {
            return;
        }

        var view = drag.View;
        var localX = x - view.Box.X;
        var localY = y - view.Box.Y;
        drag.X = localX;
        drag.Y = localY;
        drag.Tracker.Add(localX, localY, (long)(uint)(timeMs - drag.StartTime) * 1_000_000);
        var dx = localX - drag.StartX;
        var dy = localY - drag.StartY;
        if (drag.Axis == DragAxis.Undecided)
        {
            if (Math.Sqrt((dx * dx) + (dy * dy)) < TapSlop)
            {
                return;
            }

            StopHold(drag);
            if (Math.Abs(dy) > Math.Abs(dx) && drag.Card is not null)
            {
                drag.Axis = DragAxis.Vertical;
                drag.StartPose = drag.Card.Current;
            }
            else if (drag.Card is { } hit && view.Deck.StackOf(hit) is { Count: > 1 } stack && ReferenceEquals(stack, view.Deck.ActiveStack))
            {
                drag.Axis = DragAxis.Stack;
                drag.StartPosition = stack.Position;
            }
            else
            {
                drag.Axis = DragAxis.Row;
            }
        }

        switch (drag.Axis)
        {
            case DragAxis.Row:
                view.Scroll = drag.StartScroll + dx;
                FollowLayout(view);
                break;
            case DragAxis.Stack when view.Deck.ActiveStack is { } active:
                var wanted = drag.StartPosition - (dx * view.Metrics.PositionsPerPixel);
                var clamped = active.ClampPosition(wanted);
                active.Position = clamped;
                view.Scroll = drag.StartScroll - ((wanted - clamped) / view.Metrics.PositionsPerPixel);
                FollowLayout(view);
                break;
            case DragAxis.Vertical when drag.Card is { } card:
                card.Follow(drag.StartPose with { Y = drag.StartPose.Y + dy });
                break;
            case DragAxis.Reorder when drag.Card is { } held:
                held.Follow(drag.StartPose with { X = drag.StartPose.X + dx, Y = drag.StartPose.Y + dy, Scale = drag.StartPose.Scale * 1.05, Rotation = 0 });
                break;
        }

        view.Scheduler?.ScheduleRepaint();
    }

    internal void ShellRelease(int id, uint timeMs)
    {
        if (_drag is not { } drag || drag.Id != id)
        {
            return;
        }

        _drag = null;
        StopHold(drag);
        var view = drag.View;
        var (vx, vy) = drag.Tracker.Velocity((long)(uint)(timeMs - drag.StartTime) * 1_000_000);
        var dy = drag.Y - drag.StartY;
        switch (drag.Axis)
        {
            case DragAxis.Undecided:
                Tap(view, drag.Card);
                break;
            case DragAxis.Row:
                SettleRow(view, vx);
                break;
            case DragAxis.Stack when view.Deck.ActiveStack is { } stack:
                var position = stack.ClampPosition(stack.Position - (vx / CardMetrics.VelocityPerPosition));
                stack.Position = position;
                var index = (int)Math.Clamp(Math.Round(position + 0.5), 0, stack.Count - 1);
                stack.Active = stack.Cards[index];
                if (Math.Abs(view.Scroll) > view.Metrics.ActiveCardWidth / 3)
                {
                    SettleRow(view, vx);
                    break;
                }

                view.Scroll = 0;
                LayoutCards(view, animate: true, spec: LunaMotion.CardSlide);
                break;
            case DragAxis.Vertical when drag.Card is { } card:
                ReleaseVertical(view, card, dy, vx, vy);
                break;
            case DragAxis.Reorder when drag.Card is { } held:
                DropReorder(view, held, drag.X);
                break;
            default:
                LayoutCards(view, animate: true);
                break;
        }
    }

    internal void ShellCancel(int id)
    {
        if (_drag is { } drag && drag.Id == id)
        {
            CancelDrag();
        }
    }

    private void CancelDrag()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null;
        StopHold(drag);
        drag.View.Scroll = 0;
        LayoutCards(drag.View, animate: true);
    }

    private bool AnyDragOn(LunaView view) => _drag is { } drag && ReferenceEquals(drag.View, view);

    private static void StopHold(CardDrag drag)
    {
        drag.HoldTimer?.Remove();
        drag.HoldTimer = null;
    }

    private void BeginReorder(CardDrag drag)
    {
        StopHold(drag);
        if (!ReferenceEquals(_drag, drag) || drag.Axis != DragAxis.Undecided || drag.Card is not { } card)
        {
            return;
        }

        drag.Axis = DragAxis.Reorder;
        drag.StartPose = card.Current;
        drag.StartX = drag.X;
        drag.StartY = drag.Y;
        card.Follow(drag.StartPose with { Scale = drag.StartPose.Scale * 1.05, Rotation = 0 });
        card.Slot.RaiseToTop();
        drag.View.Scheduler?.ScheduleRepaint();
        _report.Line($"REORDER begin {card.Id}");
    }

    private void DropReorder(LunaView view, Card card, double x)
    {
        if ((x < 0 || x >= view.Box.Width) && NeighborView(view, x) is { } other)
        {
            MoveCardToView(card, view, other);
            return;
        }

        var deck = view.Deck;
        var target = deck.StackIndexAt(view.Metrics, x);
        var source = deck.StackOf(card);
        if (target >= 0 && !ReferenceEquals(deck.Stacks[target], source))
        {
            deck.MoveToStack(card, target, deck.Stacks[target].Count);
            _report.Line($"REORDER {card.Id} stack={deck.ActiveIndex}");
        }
        else if (target < 0)
        {
            var insert = 0;
            for (var i = 0; i < deck.Stacks.Count; i++)
            {
                if (deck.Stacks[i].X < x)
                {
                    insert = i + 1;
                }
            }

            deck.MoveToNewStack(card, insert);
            _report.Line($"REORDER {card.Id} new={deck.ActiveIndex}");
        }
        else
        {
            _report.Line($"REORDER {card.Id} stay");
        }

        deck.Activate(card);
        view.Scroll = 0;
        LayoutCards(view, animate: true, spec: LunaMotion.CardShuffleReorder);
    }

    private LunaView? NeighborView(LunaView view, double x)
    {
        var layoutX = view.Box.X + x;
        var layoutY = view.Box.Y + (view.Box.Height / 2.0);
        foreach (var other in _views)
        {
            if (!ReferenceEquals(other, view) && layoutX >= other.Box.X && layoutX < other.Box.Right && layoutY >= other.Box.Y && layoutY < other.Box.Bottom)
            {
                return other;
            }
        }

        foreach (var other in _views)
        {
            if (!ReferenceEquals(other, view) && (x < 0 ? other.Box.Right <= view.Box.X : other.Box.X >= view.Box.Right))
            {
                return other;
            }
        }

        return null;
    }

    private void MoveCardToView(Card card, LunaView from, LunaView to)
    {
        from.Deck.Remove(card);
        to.Deck.Add(card, null);
        card.Home = to;
        card.Slot.Reparent(to.Cards);
        SizeCard(card, to);
        to.InCardView = true;
        from.Scroll = 0;
        to.Scroll = 0;
        if (from.Deck.IsEmpty)
        {
            from.InCardView = true;
        }

        LayoutCards(from, animate: true, spec: LunaMotion.CardShuffleReorder);
        to.Metrics = MetricsFor(to);
        to.Deck.LayoutCardView(to.Metrics, 0);
        card.Jump(card.Target with { X = card.Target.X + (ReferenceEquals(to, NeighborView(from, -1)) ? to.Box.Width : -to.Box.Width) });
        LayoutCards(to, animate: true, spec: LunaMotion.CardShuffleReorder);
        AnnounceScales();
        _report.Line($"CARD move {card.Id} {to.Output.Name}");
    }

    private void Tap(LunaView view, Card? card)
    {
        if (card is null)
        {
            return;
        }

        var deck = view.Deck;
        if (ReferenceEquals(deck.StackOf(card), deck.ActiveStack))
        {
            deck.Activate(card);
            Maximize(view);
            return;
        }

        deck.Activate(card);
        view.Scroll = 0;
        LayoutCards(view, animate: true, spec: LunaMotion.CardSlide);
        _report.Line($"STACK open {deck.ActiveIndex}");
    }

    private void SettleRow(LunaView view, double velocityX)
    {
        var deck = view.Deck;
        var index = deck.ActiveIndex;
        if (Math.Abs(velocityX) > 500)
        {
            index += velocityX < 0 ? 1 : -1;
        }
        else
        {
            var center = view.Metrics.Width / 2.0;
            var best = double.MaxValue;
            for (var i = 0; i < deck.Stacks.Count; i++)
            {
                var distance = Math.Abs(deck.Stacks[i].X - center);
                if (distance < best)
                {
                    best = distance;
                    index = i;
                }
            }
        }

        deck.ActivateStack(Math.Clamp(index, 0, Math.Max(0, deck.Stacks.Count - 1)));
        view.Scroll = 0;
        LayoutCards(view, animate: true, spec: LunaMotion.CardSlide);
        _report.Line($"ROW {deck.ActiveIndex}");
    }

    private void ReleaseVertical(LunaView view, Card card, double dy, double vx, double vy)
    {
        var half = view.Metrics.CardHeight * card.Current.Scale / 2;
        switch (CardFlick.Decide(dy, vy, card.Current.Y, half, view.Metrics.Height))
        {
            case FlickOutcome.Close:
                FlickClose(view, card, vy);
                return;
            case FlickOutcome.Maximize:
                view.Deck.Activate(card);
                Maximize(view);
                return;
        }

        view.Deck.LayoutCardView(view.Metrics, view.Scroll);
        card.SpringTo(card.Target, vx, vy, Now);
        view.Scheduler?.ScheduleRepaint();
        _report.Line($"FLICK {card.Id} back");
    }

    private void FollowLayout(LunaView view)
    {
        view.Deck.LayoutCardView(view.Metrics, view.Scroll);
        foreach (var stack in view.Deck)
        {
            foreach (var card in stack)
            {
                if (_drag is { Axis: DragAxis.Reorder or DragAxis.Vertical } drag && ReferenceEquals(drag.Card, card))
                {
                    continue;
                }

                card.Follow(card.Target);
            }
        }
    }

    private static Card? CardAt(LunaView view, double x, double y)
    {
        var stacks = view.Deck.Stacks;
        Card? hit = null;
        foreach (var stack in stacks)
        {
            foreach (var card in stack)
            {
                if (Contains(card, x, y))
                {
                    hit = card;
                }
            }
        }

        if (view.Deck.ActiveStack is { } active)
        {
            for (var i = active.Count - 1; i >= 0; i--)
            {
                if (Contains(active.Cards[i], x, y))
                {
                    return active.Cards[i];
                }
            }
        }

        return hit;
    }

    private static bool Contains(Card card, double x, double y)
    {
        var pose = card.Current;
        if (pose.Scale <= 0)
        {
            return false;
        }

        var radians = -pose.Rotation * Math.PI / 180;
        var dx = x - pose.X;
        var dy = y - pose.Y;
        var lx = ((dx * Math.Cos(radians)) - (dy * Math.Sin(radians))) / pose.Scale;
        var ly = ((dx * Math.Sin(radians)) + (dy * Math.Cos(radians))) / pose.Scale;
        return Math.Abs(lx) <= card.Width / 2.0 && Math.Abs(ly) <= card.Height / 2.0;
    }

    internal bool ShellAxis(double x, double y, PointerAxis axis)
    {
        var view = ViewAt(x, y);
        if (!view.InCardView || view.Deck.IsEmpty || _locked)
        {
            return false;
        }

        int steps;
        if (axis.Value120 != 0)
        {
            _axisAccum = 0;
            steps = Math.Sign(axis.Value120);
        }
        else
        {
            _axisAccum += axis.Value;
            if (Math.Abs(_axisAccum) < 60)
            {
                return true;
            }

            steps = Math.Sign(_axisAccum);
            _axisAccum = 0;
        }

        view.Deck.ActivateStack(Math.Clamp(view.Deck.ActiveIndex + Math.Sign(steps), 0, view.Deck.Stacks.Count - 1));
        view.Scroll = 0;
        LayoutCards(view, animate: true, spec: LunaMotion.CardSlide);
        _report.Line($"ROW {view.Deck.ActiveIndex}");
        return true;
    }

    internal bool EdgeAt(double x, double y, out LunaEdge edge, out double scale)
    {
        scale = 1;
        edge = LunaEdge.None;
        if (_locked || _views.Count == 0)
        {
            return false;
        }

        var view = ViewAt(x, y);
        var box = view.Box;
        var band = _config.EdgeBand;
        if (KeyboardCovers(x, y))
        {
            return false;
        }

        if (y >= box.Bottom - band)
        {
            edge = LunaEdge.Bottom;
        }
        else if (view.BarHidden && !view.BarRevealed && y < box.Y + band)
        {
            edge = LunaEdge.Top;
        }
        else if (!view.InCardView && x < box.X + band)
        {
            edge = LunaEdge.Left;
        }
        else if (!view.InCardView && x >= box.Right - band)
        {
            edge = LunaEdge.Right;
        }

        if (edge != LunaEdge.None)
        {
            _edge = new EdgeState
            {
                View = view,
                Edge = edge,
                RaisesKeyboard = edge == LunaEdge.Bottom && _config.Keyboard.Enabled && !view.InCardView &&
                    view.Deck.ActiveCard is not null && x >= box.Right - (box.Width * _config.Keyboard.ManualEdge),
            };
        }

        return edge != LunaEdge.None;
    }

    internal bool EdgeClaim(LunaEdge edge, EdgeAxis axis, double x, double y, uint timeMs)
    {
        if (_edge.View is not { } view)
        {
            return false;
        }

        if (edge == LunaEdge.Bottom && axis == EdgeAxis.Along && (view.InCardView || !_config.NextPrevious))
        {
            return false;
        }

        _edge.Axis = axis;
        _edge.X0 = x;
        _edge.Y0 = y;
        _edge.X = x;
        _edge.Y = y;
        _edge.StartTime = timeMs;
        _edge.QuickLaunch = false;
        if (edge == LunaEdge.Bottom && axis == EdgeAxis.Inward && !_edge.RaisesKeyboard && !KeyboardShown)
        {
            var timer = _host.Loop.AddTimer(FireQuickLaunch);
            timer.UpdateTimer(Math.Max(1, _config.QuickLaunchHoldMs));
            _edge.HoldTimer = timer;
            _edge.RestX = x;
            _edge.RestY = y;
        }

        _report.Line($"EDGE {edge.ToString().ToLowerInvariant()} {(axis == EdgeAxis.Inward ? "in" : "along")}");
        return true;
    }

    private void FireQuickLaunch()
    {
        _edge.HoldTimer?.Remove();
        _edge.HoldTimer = null;
        if (_edge.View is not { } view || _edge.Edge != LunaEdge.Bottom)
        {
            return;
        }

        _edge.QuickLaunch = true;
        ShowQuickLaunch(view, true, follow: true);
        DockFollow(view, _edge.X - view.Box.X, _edge.Y - view.Box.Y);
        _report.Line("QUICKLAUNCH hold");
    }

    internal void EdgeMove(double x, double y, uint timeMs)
    {
        _edge.X = x;
        _edge.Y = y;
        if (_edge.QuickLaunch && _edge.View is { } view)
        {
            DockFollow(view, x - view.Box.X, y - view.Box.Y);
            return;
        }

        if (_edge.HoldTimer is { } hold && Math.Abs(x - _edge.RestX) + Math.Abs(y - _edge.RestY) > HoldSlop)
        {
            _edge.RestX = x;
            _edge.RestY = y;
            hold.UpdateTimer(Math.Max(1, _config.QuickLaunchHoldMs));
        }
    }

    internal void EdgeEnd(double velocityX, double velocityY, uint timeMs, bool cancelled)
    {
        var state = _edge;
        state.HoldTimer?.Remove();
        _edge = default;
        _report.Line($"EDGE end{(cancelled ? " cancelled" : "")}");
        if (state.View is not { } view || cancelled)
        {
            if (state.QuickLaunch && state.View is { } dock)
            {
                DockRelease(dock, double.NaN, double.NaN);
            }

            return;
        }

        var dx = state.X - state.X0;
        var dy = state.Y - state.Y0;
        switch (state.Edge)
        {
            case LunaEdge.Bottom when state.RaisesKeyboard && state.Axis == EdgeAxis.Inward:
                if (dy < 0)
                {
                    RaiseKeyboard();
                }

                break;
            case LunaEdge.Bottom when state.QuickLaunch:
                DockRelease(view, state.X - view.Box.X, state.Y - view.Box.Y);
                break;
            case LunaEdge.Bottom when state.Axis == EdgeAxis.Along:
                if (Math.Abs(dx) > view.Box.Width / 2.0 || Math.Abs(velocityX) > 1500)
                {
                    SwitchCard(view, dx < 0 ? 1 : -1);
                }

                break;
            case LunaEdge.Bottom:
                if (velocityY > 300 && dy > 0)
                {
                    break;
                }

                if (LauncherOpen(view) || view.InCardView)
                {
                    ShowLauncher(view, !LauncherOpen(view));
                }
                else
                {
                    EnterCardView(view);
                }

                break;
            case LunaEdge.Left:
            case LunaEdge.Right:
                Back(view);
                break;
            case LunaEdge.Top:
                view.BarRevealed = true;
                FadeStatusBar(view);
                break;
        }
    }

    internal bool PinchAccepts(double x, double y)
    {
        if (_locked || _views.Count == 0)
        {
            return false;
        }

        if (KeyboardCovers(x, y))
        {
            return false;
        }

        var view = ViewAt(x, y);
        if (view.InCardView)
        {
            return CardAt(view, x - view.Box.X, y - view.Box.Y) is not null;
        }

        return view.Deck.ActiveCard is not null;
    }

    internal bool PinchBegin(double x, double y, uint timeMs)
    {
        var view = ViewAt(x, y);
        var card = view.InCardView ? CardAt(view, x - view.Box.X, y - view.Box.Y) : view.Deck.ActiveCard;
        if (card is null)
        {
            return false;
        }

        if (_drag is not null)
        {
            CancelDrag();
        }

        _pinchView = view;
        _pinchCard = card;
        _pinchFromMaximized = !view.InCardView;
        _pinchProgress = 0;
        view.Deck.Activate(card);
        view.Metrics = MetricsFor(view);
        view.Deck.LayoutCardView(view.Metrics, 0);
        CopyPoses(view, toA: true);
        view.Deck.LayoutMaximized(view.Metrics, card.Fullscreen);
        CopyPoses(view, toA: false);
        foreach (var stack in view.Deck)
        {
            foreach (var each in stack)
            {
                each.Slot.Enabled = true;
            }
        }

        _report.Line($"PINCH begin {card.Id} {(_pinchFromMaximized ? "in" : "out")}");
        return true;
    }

    internal void PinchUpdate(double scale, uint timeMs)
    {
        if (_pinchView is not { } view || _pinchCard is null)
        {
            return;
        }

        var cardScale = view.Metrics.ActiveScale;
        _pinchProgress = _pinchFromMaximized
            ? Math.Clamp((1 - scale) / (1 - cardScale), 0, 1)
            : Math.Clamp((scale - 1) / ((1 / cardScale) - 1), 0, 1);
        var t = _pinchFromMaximized ? 1 - _pinchProgress : _pinchProgress;
        foreach (var stack in view.Deck)
        {
            foreach (var card in stack)
            {
                card.Follow(CardPose.Lerp(card.PoseA, card.PoseB, t));
            }
        }

        view.Scheduler?.ScheduleRepaint();
    }

    internal void PinchEnd(double scalePerSecond, uint timeMs, bool cancelled)
    {
        if (_pinchView is not { } view)
        {
            return;
        }

        var commit = !cancelled && (_pinchProgress > 0.5 ||
            (_pinchFromMaximized ? scalePerSecond < -1.5 : scalePerSecond > 1.5));
        _pinchView = null;
        _pinchCard = null;
        var toCardView = _pinchFromMaximized == commit;
        view.InCardView = toCardView;
        SyncBarVisibility(view);
        LayoutCards(view, animate: true, spec: toCardView ? LunaMotion.CardMinimize : LunaMotion.CardMaximize);
        if (toCardView)
        {
            Seat.Keyboard.NotifyClearFocus();
        }
        else if (view.Deck.ActiveCard is { } active)
        {
            Focus(active);
        }

        _report.Line($"PINCH end {(commit ? "commit" : "back")} {(toCardView ? "cardview" : "maximized")}");
    }

    private static void CopyPoses(LunaView view, bool toA)
    {
        foreach (var stack in view.Deck)
        {
            foreach (var card in stack)
            {
                if (toA)
                {
                    card.PoseA = card.Target;
                }
                else
                {
                    card.PoseB = card.Target;
                }
            }
        }
    }

    internal bool PadSwipeBegin(uint fingers, double x, double y, uint timeMs)
    {
        if (_locked || fingers is not (3 or 4) || _views.Count == 0)
        {
            return false;
        }

        _padFingers = fingers;
        _padX = 0;
        _padY = 0;
        _padView = ViewAt(x, y);
        return true;
    }

    internal void PadSwipeUpdate(double dx, double dy, uint timeMs)
    {
        _padX += dx;
        _padY += dy;
    }

    internal void PadSwipeEnd(bool cancelled, uint timeMs)
    {
        if (cancelled || _padView is not { } view)
        {
            _padView = null;
            return;
        }

        _padView = null;
        const double threshold = 50;
        if (Math.Abs(_padY) >= Math.Abs(_padX) && Math.Abs(_padY) >= threshold)
        {
            if (_padY < 0)
            {
                RunAction(view, _padFingers == 4 ? "quick_launch" : view.InCardView ? "launcher" : "card_view");
            }
            else if (_padFingers == 3)
            {
                RunAction(view, "maximize");
            }
        }
        else if (Math.Abs(_padX) >= threshold && _padFingers == 3)
        {
            RunAction(view, _padX < 0 ? "next" : "previous");
        }
    }

    internal bool PadPinchBegin(uint fingers, double x, double y, uint timeMs)
    {
        if (_locked || fingers != 2 || !PinchAccepts(x, y))
        {
            return false;
        }

        _padPinchScale = 1;
        return PinchBegin(x, y, timeMs);
    }

    internal void PadPinchUpdate(double scale, uint timeMs)
    {
        _padPinchScale = scale;
        PinchUpdate(scale, timeMs);
    }

    internal void PadPinchEnd(bool cancelled, uint timeMs) => PinchEnd(0, timeMs, cancelled);
}
