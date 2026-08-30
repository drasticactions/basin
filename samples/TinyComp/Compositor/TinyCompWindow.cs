using System.Diagnostics;
using Basin;
using Basin.Host;
using Basin.Backend.Libinput;
using Basin.Cli;
using Basin.Effects;
using Basin.Backend.Wayland;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Capabilities;
using Basin.UI.Skia;
using Wayland;
using Wayland.Server;

using Basin.Diagnostics;

namespace TinyComp;

internal sealed partial class TinyComp
{
    internal sealed class Window : IGrabTarget
    {
        private readonly TinyComp _comp;

        public Window(TinyComp comp, XdgToplevelWindow toplevel)
        {
            _comp = comp;
            Toplevel = toplevel;
            toplevel.Xdg.Mapped += () =>
            {
                Rule = comp._config.RuleFor(toplevel.AppId, toplevel.Title);
                CornerRadius = Rule?.CornerRadius ?? comp._cornerRadius;
                Workspace = comp.WorkspaceForRule(Rule) ?? comp.CurrentWorkspace();
                Tree = new SceneTree(Workspace?.Tree ?? comp._layers.Windows);
                Tree.SetPosition(X, Y);
                SceneSurface = new SceneSurface(Tree, toplevel.Surface);
                comp.RefreshSurfaceLuts();
                comp.ApplyBlur(SceneSurface);
                comp.ApplyCorners(SceneSurface, CornerRadius);
                comp.OnWindowMapped(this);
                comp._effects.CancelClosing(toplevel.Surface);
                SetDecorated(comp.IsServerDecorated(toplevel));
                ReportGeometry();
                SetShadow(comp._shadowTexture);
                comp._feedback?.OnMapped();
                comp._effects.OnMapped(Tree, Rule?.OpenFor(comp._effects.OpenKind));
            };
            toplevel.Xdg.Unmapped += () =>
            {
                comp._effects.OnClosing(
                    toplevel.Surface, Tree, comp._layers.Top, _cornerRig, Rule?.CloseFor(comp._effects.CloseKind));
                _cornerRig = null;
                _shadow?.Dispose();
                _shadow = null;
                SceneSurface?.Destroy();
                SceneSurface = null;
                _frame?.Dispose();
                _frame = null;
                if (Tree is { } gone)
                {
                    comp._effects.Forget(gone);
                }

                Tree?.Destroy();
                Tree = null;
                comp.OnWindowGone(this);
            };
            toplevel.Xdg.Committed += ApplyResizeAnchor;
            toplevel.Xdg.Committed += LayoutDecorations;
            toplevel.Xdg.Committed += LayoutShadow;
            toplevel.Xdg.Committed += ReportGeometry;
            toplevel.TitleChanged += RefreshFrame;
            toplevel.AppIdChanged += RefreshFrame;
            toplevel.MinimizeRequested += () => comp.SetMinimized(this, true);
            toplevel.MoveRequested += serial => comp.BeginMove(this, serial);
            toplevel.ResizeRequested += (serial, edges) => comp.BeginResize(this, edges, serial);
            toplevel.MaximizeRequested += maximized =>
            {
                toplevel.RequestConfigure();
                if (maximized != toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized))
                {
                    ApplyMaximize(maximized);
                }
            };
            toplevel.FullscreenRequested += fullscreen =>
            {
                toplevel.RequestConfigure();
                if (fullscreen == toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen))
                {
                    return;
                }

                toplevel.SetFullscreen(fullscreen);
                if (fullscreen)
                {
                    var output = comp._layout.OutputAt(comp._cursorX, comp._cursorY) ?? comp.Views[0].Output;
                    var box = comp._layout.BoxOf(output);
                    _restore = (X, Y);
                    MoveTo(box.X, box.Y);
                    toplevel.SetSize(box.Width, box.Height);
                }
                else
                {
                    MoveTo(_restore.X, _restore.Y);
                    toplevel.SetSize(0, 0);
                }
            };
        }

        public XdgToplevelWindow Toplevel { get; }

        public Rule? Rule { get; private set; }

        public int CornerRadius { get; private set; }

        public Workspace? Workspace { get; set; }

        public bool Minimized { get; set; }

        public SceneTree? Tree { get; private set; }

        public SceneTree? EffectTree => Tree;

        public SceneSurface? SceneSurface { get; private set; }

        public int X { get; private set; }

        public int Y { get; private set; }

        private (int X, int Y) _restore;

        public void MoveTo(int x, int y)
        {
            X = x;
            Y = y;
            Tree?.SetPosition(x, y);
            ReportGeometry();
            _comp._workspaceModel.RaiseMembersChanged();

            if (_frame is not null && Tree is not null && _comp.ScaleForWindow(this) != _frameScale)
            {
                LayoutDecorations();
            }
        }

        private double _frameScale = 1.0;

        public (int Width, int Height) GeometrySize
        {
            get
            {
                var geometry = Toplevel.Xdg.EffectiveGeometry;
                return (geometry.Width, geometry.Height);
            }
        }

        public void ResizeTo(int x, int y, int width, int height, ResizeEdges edges)
        {
            var (wasWidth, wasHeight) = GeometrySize;
            _comp._effects.OnResized(
                Tree,
                new Box(0, 0, Math.Max(width, 1), Math.Max(height, 1)),
                new Box(0, 0, Math.Max(wasWidth, 1), Math.Max(wasHeight, 1)),
                new Box(0, 0, Math.Max(width, 1), Math.Max(height, 1)),
                0,
                0);
            _resizeAnchor = ResizeAnchor.For(edges, x, y, width, height);
            if (_resizeAnchor is null)
            {
                MoveTo(x, y);
            }

            Toplevel.SetSize(width, height);
            ReportGeometry();
            _comp._workspaceModel.RaiseMembersChanged();

            if (_frame is not null && width > 0 && height > 0 &&
                !Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen))
            {
                _pendingFrameSize = (width, height);
                ScheduleFrameConfigure();
            }
        }

        private (int Width, int Height)? _pendingFrameSize;
        private bool _frameConfigureScheduled;

        private void ScheduleFrameConfigure()
        {
            if (_frameConfigureScheduled)
            {
                return;
            }

            _frameConfigureScheduled = true;
            _comp.Loop.AddIdle(() =>
            {
                _frameConfigureScheduled = false;
                if (_frame is not { } frame || _pendingFrameSize is not { } size)
                {
                    return;
                }

                _pendingFrameSize = null;
                var g = Toplevel.Xdg.EffectiveGeometry;
                frame.Configure(new Box(g.X, g.Y, size.Width, size.Height), _comp.ScaleAt(X + 1, Y + 1), BuildState());
            });
        }

        public void SetResizing(bool resizing)
        {
            _resizing = resizing;
            Toplevel.SetResizing(resizing);
        }

        private bool _resizing;

        private ResizeAnchor? _resizeAnchor;

        private void ApplyResizeAnchor()
        {
            if (_resizeAnchor is not { } anchor)
            {
                return;
            }

            var (width, height) = GeometrySize;
            var (x, y) = anchor.PositionFor(width, height, X, Y);
            if (x != X || y != Y)
            {
                MoveTo(x, y);
            }

            _resizeAnchor = ResizeAnchor.AfterCommit(_resizeAnchor, _resizing);
        }

        private RestoreGeometry _maximizeRestore;

        public void ToggleMaximize() =>
            ApplyMaximize(!Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized));

        private void ApplyMaximize(bool maximized)
        {
            Toplevel.SetMaximized(maximized);
            if (!maximized)
            {
                if (_maximizeRestore.TryGet(out var saved))
                {
                    _maximizeRestore = RestoreGeometry.None;
                    MoveTo(saved.X, saved.Y);
                    Toplevel.SetSize(saved.Width, saved.Height);
                }
                else
                {
                    Toplevel.SetSize(0, 0);
                    _comp.PlaceCascade(this);
                }

                return;
            }

            var comp = _comp;
            var view = comp.Views.FirstOrDefault(v => comp._layout.OutputAt(comp._cursorX, comp._cursorY) == v.Output)
                ?? comp.Views[0];
            var geometry = Toplevel.Xdg.EffectiveGeometry;
            _maximizeRestore = _maximizeRestore.Saving(new Box(X, Y, geometry.Width, geometry.Height));
            ApplyMaximizeGeometry(view);
        }

        private void ApplyMaximizeGeometry(OutputView view)
        {
            var comp = _comp;
            var box = comp._layout.BoxOf(view.Output);
            var usable = view.UsableArea.IsEmpty ? box with { X = 0, Y = 0 } : view.UsableArea;
            var insets = _frame?.Measure(BuildState(), comp.ScaleAt(X + 1, Y + 1)) ?? default;
            MoveTo(box.X + usable.X + insets.Left, box.Y + usable.Y + insets.Top);
            Toplevel.SetSize(usable.Width - insets.Left - insets.Right, usable.Height - insets.Top - insets.Bottom);
        }

        public void ReapplyPinnedGeometry()
        {
            var comp = _comp;
            if (comp.Views.Count == 0)
            {
                return;
            }

            var view = comp.Views.FirstOrDefault(v => comp._layout.OutputAt(X + 1, Y + 1) == v.Output)
                ?? comp.Views[0];
            if (Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen))
            {
                var box = comp._layout.BoxOf(view.Output);
                MoveTo(box.X, box.Y);
                Toplevel.SetSize(box.Width, box.Height);
            }
            else if (Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized))
            {
                ApplyMaximizeGeometry(view);
            }
        }

        private Frame? _frame;
        private FrameCornerRig? _cornerRig;
        private bool _active;
        private string? _iconName;

        public Frame? Frame => _frame;

        public void SetDecorated(bool decorated)
        {
            _comp._xdgToplevels.SetDecoration(Toplevel, noBorder: !decorated, userCanSet: true);
            if (!decorated)
            {
                _cornerRig?.Dispose();
                _cornerRig = null;
                _frame?.Dispose();
                _frame = null;
                if (SceneSurface is not null)
                {
                    _comp.ApplyCorners(SceneSurface, CornerRadius);
                }

                ReportGeometry();
                return;
            }

            if (Tree is null || _frame is not null
                || _comp.CreateFrameRenderer(Rule?.FrameStyle ?? _comp._frameStyle) is not { } renderer)
            {
                return;
            }

            _frame = new Frame(_comp.UIHost, renderer, Tree)
            {
                MenuLayer = _comp._layers.Overlay,
                TouchSlop = TouchGripSlop,
            };
            _frame.Requested += OnFrameAction;
            _frame.Faulted += e => _comp.Log.Error($"frame fault {Toplevel.AppId}: {e.Message}");
            SceneSurface?.Tree.RaiseToTop();
            LayoutDecorations();
            ReportGeometry();
            if (CornerRadius > 0 && SceneSurface is not null)
            {
                _cornerRig = new FrameCornerRig(_comp._renderer, _frame, SceneSurface.Content, CornerRadius);
            }
        }

        public void RebuildFrame()
        {
            if (_frame is null)
            {
                return;
            }

            _cornerRig?.Dispose();
            _cornerRig = null;
            _frame.Dispose();
            _frame = null;
            SetDecorated(true);
        }

        public void SetDecorationFocus(bool active)
        {
            _active = active;
            RefreshFrame();
        }

        public void SetDimmed(bool dimmed)
        {
            if (_dimmed == dimmed || SceneSurface is null)
            {
                return;
            }

            _dimmed = dimmed;
            if (dimmed)
            {
                SceneSurface.Content.TextureShader = _comp._dimShader;
            }
            else
            {
                _comp.ApplyCorners(SceneSurface, CornerRadius);
            }
        }

        public void SetShadow(Basin.Effects.DropShadowTexture? texture)
        {
            if (texture is null)
            {
                _shadow?.Dispose();
                _shadow = null;
                return;
            }

            if (Tree is null)
            {
                return;
            }

            _shadow ??= new Basin.Effects.DropShadowEffect(Tree);
            _shadow.Texture = texture;
            var (width, height) = GeometrySize;
            _shadow.SetGeometry(new Box(0, 0, Math.Max(width, 1), Math.Max(height, 1)));
        }

        internal void LayoutShadow()
        {
            if (_shadow is null)
            {
                return;
            }

            var (width, height) = GeometrySize;
            _shadow.SetGeometry(new Box(0, 0, Math.Max(width, 1), Math.Max(height, 1)));
        }

        private bool _dimmed;
        private Basin.Effects.DropShadowEffect? _shadow;

        public void SetIconName(string? name)
        {
            _iconName = name;
            RefreshFrame();
        }

        public Box FrameBox
        {
            get
            {
                if (_frame is null)
                {
                    return default;
                }

                var g = Toplevel.Xdg.EffectiveGeometry;
                var insets = _frame.Measure(BuildState(), _comp.ScaleAt(X + 1, Y + 1));
                return new Box(
                    X + g.X - insets.Left,
                    Y + g.Y - insets.Top,
                    g.Width + insets.Left + insets.Right,
                    g.Height + insets.Top + insets.Bottom);
            }
        }

        public void ReportGeometry()
        {
            if (Tree is null)
            {
                return;
            }

            var g = Toplevel.Xdg.EffectiveGeometry;
            var client = new Box(X + g.X, Y + g.Y, Math.Max(g.Width, 1), Math.Max(g.Height, 1));
            var frame = _frame is null ? client : FrameBox;
            _comp._xdgToplevels.SetGeometry(Toplevel, frame, client);
        }

        private void LayoutDecorations()
        {
            if (_frame is null)
            {
                return;
            }

            var geometry = Toplevel.Xdg.EffectiveGeometry;
            var visible = geometry.Width > 0 && geometry.Height > 0 &&
                !Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen);
            _frame.Visible = visible;
            if (!visible)
            {
                return;
            }

            var scale = _comp.ScaleForWindow(this);
            _frameScale = scale;
            if (!_frame.HasPendingFor(geometry, scale))
            {
                _frame.Configure(geometry, scale, BuildState());
            }

            _frame.Commit();
        }

        internal Box ScaleBox
        {
            get
            {
                var g = Toplevel.Xdg.EffectiveGeometry;
                return new Box(X + g.X, Y + g.Y, Math.Max(g.Width, 1), Math.Max(g.Height, 1));
            }
        }

        public void RefreshFrame()
        {
            if (_frame is null || Tree is null)
            {
                return;
            }

            LayoutDecorations();
        }

        private FrameState BuildState() => new()
        {
            Title = Toplevel.Title,
            AppId = Toplevel.AppId,
            Icon = new FrameIcon(_iconName, null),
            Active = _active,
            Maximized = Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized),
            Fullscreen = Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen),
            Resizing = Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Resizing),
            Capabilities = FrameCapabilities.Maximize | FrameCapabilities.Minimize,
        };

        private void OnFrameAction(FrameAction action)
        {
            switch (action.Kind)
            {
                case FrameActionKind.Close:
                    Toplevel.Close();
                    break;
                case FrameActionKind.ToggleMaximize:
                    ToggleMaximize();
                    break;
                case FrameActionKind.Minimize:
                    _comp.SetMinimized(this, true);
                    break;
                case FrameActionKind.Move:
                    _comp.BeginMove(this);
                    break;
                case FrameActionKind.Resize:
                    _comp.BeginResize(this, (ResizeEdges)action.Edges);
                    break;
            }
        }

        public bool Owns(Surface surface)
        {
            for (var candidate = surface; candidate is not null;)
            {
                if (candidate == Toplevel.Surface)
                {
                    return true;
                }

                candidate = candidate.SubsurfaceRole?.Parent;
            }

            return false;
        }
    }
}
