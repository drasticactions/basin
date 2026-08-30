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
    internal sealed class XWindow : IGrabTarget
    {
        private readonly TinyComp _comp;
        private Frame? _frame;
        private FrameCornerRig? _cornerRig;
        private bool _active;
        private bool _maximized;
        private RestoreGeometry _restore;

        public XWindow(TinyComp comp, Basin.XWayland.XWaylandWindow xwin, SceneSurface scene, bool framable)
        {
            _comp = comp;
            XWin = xwin;
            Framable = framable;
            Rule = framable ? comp._config.RuleFor(xwin.Class, xwin.Title) : null;
            CornerRadius = Rule?.CornerRadius ?? comp._cornerRadius;
            Tree = scene.Tree.Parent!;
            Tree.SetPosition(xwin.X, xwin.Y);
            SceneSurface = scene;
            scene.Tree.SetPosition(0, 0);
            comp.RefreshSurfaceLuts();
            comp.ApplyCorners(SceneSurface, CornerRadius);
            SetShadow(framable ? comp._shadowTexture : null);
            xwin.TitleChanged += Layout;
            xwin.IconChanged += RefreshIcon;
            UpdateDecorations();
            RefreshIcon();
        }

        public Rule? Rule { get; }

        public int CornerRadius { get; }

        public void SetDimmed(bool dimmed)
        {
            if (_dimmed == dimmed || SceneSurface.IsDestroyed)
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
            if (texture is null || !Framable)
            {
                _shadow?.Dispose();
                _shadow = null;
                return;
            }

            _shadow ??= new Basin.Effects.DropShadowEffect(Tree);
            _shadow.Texture = texture;
            LayoutShadow();
        }

        internal void LayoutShadow() =>
            _shadow?.SetGeometry(new Box(0, 0, Math.Max(XWin.Width, 1), Math.Max(XWin.Height, 1)));

        internal FrameCornerRig? DetachCornerRig()
        {
            var rig = _cornerRig;
            _cornerRig = null;
            return rig;
        }

        private bool _dimmed;
        private Basin.Effects.DropShadowEffect? _shadow;

        public Frame? Frame => _frame;

        private MemoryBuffer? _iconBuffer;

        private void RefreshIcon()
        {
            _iconBuffer?.Destroy();
            _iconBuffer = null;
            if (XWin.Icon is { } icon)
            {
                var buffer = new MemoryBuffer(icon.Width, icon.Height, DrmFormat.Argb8888);
                if (buffer.BeginDataAccess(BufferDataAccess.Write, out var view))
                {
                    unsafe
                    {
                        for (var y = 0; y < icon.Height; y++)
                        {
                            var row = (uint*)(view.Data + y * view.Stride);
                            for (var x = 0; x < icon.Width; x++)
                            {
                                var argb = icon.Pixels[y * icon.Width + x];
                                var a = argb >> 24;
                                row[x] = (a << 24)
                                    | ((((argb >> 16) & 0xFF) * a / 255) << 16)
                                    | ((((argb >> 8) & 0xFF) * a / 255) << 8)
                                    | ((argb & 0xFF) * a / 255);
                            }
                        }
                    }

                    buffer.EndDataAccess();
                    _iconBuffer = buffer;
                }
                else
                {
                    buffer.Destroy();
                }
            }

            Layout();
        }

        public Basin.XWayland.XWaylandWindow XWin { get; }

        public Workspace? Workspace { get; set; }

        public SceneTree Tree { get; }

        public SceneTree? EffectTree => Tree;

        public SceneSurface SceneSurface { get; }

        public bool Framable { get; }

        public int X => XWin.X;

        public int Y => XWin.Y;

        public (int Width, int Height) GeometrySize => (XWin.Width, XWin.Height);

        public void MoveTo(int x, int y) => ResizeTo(x, y, XWin.Width, XWin.Height, ResizeEdges.None);

        public void ResizeTo(int x, int y, int width, int height, ResizeEdges edges)
        {
            _comp._effects.OnResized(
                Tree,
                new Box(0, 0, Math.Max(width, 1), Math.Max(height, 1)),
                new Box(0, 0, Math.Max(XWin.Width, 1), Math.Max(XWin.Height, 1)),
                new Box(0, 0, Math.Max(width, 1), Math.Max(height, 1)),
                0,
                0);
            XWin.Configure(x, y, width, height);
            Layout();
        }

        public void SetResizing(bool resizing)
        {
        }

        public void Layout()
        {
            Tree.SetPosition(XWin.X, XWin.Y);
            SceneSurface.Tree.SetPosition(0, 0);
            LayoutShadow();
            ReportGeometry();
            _comp._workspaceModel.RaiseMembersChanged();
            if (_frame is not null && XWin.Width > 0 && XWin.Height > 0)
            {
                _frame.Visible = true;
                _frame.Configure(new Box(0, 0, XWin.Width, XWin.Height), _comp.ScaleAt(X + 1, Y + 1), BuildState());
                _frame.Commit();
            }
        }

        public void ReportGeometry()
        {
            var client = new Box(XWin.X, XWin.Y, Math.Max(XWin.Width, 1), Math.Max(XWin.Height, 1));
            var frame = _frame is null ? client : FrameBox;
            _comp._xwaylandModule.Toplevels?.SetGeometry(XWin, frame, client);
        }

        public void SetNoBorderOverride(bool noBorder)
        {
            _noBorderOverride = noBorder;
            if (noBorder)
            {
                DisposeFrame();
                _comp.ApplyCorners(SceneSurface, CornerRadius);
                ReportDecoration();
                ReportGeometry();
            }
            else
            {
                UpdateDecorations();
            }
        }

        private bool? _noBorderOverride;

        private bool WantsFrame => Framable && (_noBorderOverride is { } o ? !o : XWin.WantsDecorations);

        private void ReportDecoration() =>
            _comp._xwaylandModule.Toplevels?.SetDecoration(XWin, noBorder: !WantsFrame, userCanSet: Framable);

        internal void DisposeFrame()
        {
            _cornerRig?.Dispose();
            _cornerRig = null;
            _frame?.Dispose();
            _frame = null;
        }

        public void RebuildFrame()
        {
            if (_frame is null)
            {
                return;
            }

            DisposeFrame();
            UpdateDecorations();
        }

        public void UpdateDecorations()
        {
            ReportDecoration();
            if (!WantsFrame)
            {
                DisposeFrame();
                _comp.ApplyCorners(SceneSurface, CornerRadius);
                ReportGeometry();
                return;
            }

            if (_frame is not null
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
            _frame.Faulted += e => _comp.Log.Error($"frame fault {XWin.Class}: {e.Message}");
            SceneSurface.Tree.RaiseToTop();
            Layout();
            if (CornerRadius > 0)
            {
                _cornerRig = new FrameCornerRig(_comp._renderer, _frame, SceneSurface.Content, CornerRadius);
            }
        }

        public void SetDecorationFocus(bool active)
        {
            _active = active;
            Layout();
        }

        public Box FrameBox
        {
            get
            {
                if (_frame is null)
                {
                    return default;
                }

                var insets = _frame.Measure(BuildState(), _comp.ScaleAt(X + 1, Y + 1));
                return new Box(
                    X - insets.Left,
                    Y - insets.Top,
                    XWin.Width + insets.Left + insets.Right,
                    XWin.Height + insets.Top + insets.Bottom);
            }
        }

        private FrameState BuildState() => new()
        {
            Title = XWin.Title,
            AppId = XWin.Class,
            Icon = new FrameIcon(null, _iconBuffer),
            Active = _active,
            Maximized = _maximized,
            Capabilities = FrameCapabilities.Maximize | FrameCapabilities.Minimize,
        };

        public bool Minimized { get; set; }

        private void OnFrameAction(FrameAction action)
        {
            switch (action.Kind)
            {
                case FrameActionKind.Close:
                    XWin.Close();
                    break;
                case FrameActionKind.Minimize:
                    _comp.SetMinimized(this, true);
                    break;
                case FrameActionKind.ToggleMaximize:
                    ToggleMaximize();
                    break;
                case FrameActionKind.Move:
                    _comp.BeginMove(this);
                    break;
                case FrameActionKind.Resize:
                    _comp.BeginResize(this, (ResizeEdges)action.Edges);
                    break;
            }
        }

        public void ToggleMaximize()
        {
            if (_maximized)
            {
                _maximized = false;
                if (_restore.TryGet(out var saved))
                {
                    _restore = RestoreGeometry.None;
                    ResizeTo(saved.X, saved.Y, saved.Width, saved.Height, ResizeEdges.None);
                }
                else
                {
                    _comp.PlaceCascade(this);
                }

                return;
            }

            var view = _comp.Views.FirstOrDefault(v => _comp._layout.OutputAt(_comp._cursorX, _comp._cursorY) == v.Output)
                ?? _comp.Views[0];
            _restore = _restore.Saving(new Box(XWin.X, XWin.Y, XWin.Width, XWin.Height));
            _maximized = true;
            ApplyMaximizeGeometry(view);
        }

        private void ApplyMaximizeGeometry(OutputView view)
        {
            var box = _comp._layout.BoxOf(view.Output);
            var usable = view.UsableArea.IsEmpty ? box with { X = 0, Y = 0 } : view.UsableArea;
            var insets = _frame?.Measure(BuildState(), _comp.ScaleAt(X + 1, Y + 1)) ?? default;
            ResizeTo(
                box.X + usable.X + insets.Left,
                box.Y + usable.Y + insets.Top,
                usable.Width - insets.Left - insets.Right,
                usable.Height - insets.Top - insets.Bottom,
                ResizeEdges.None);
        }

        public void ReapplyPinnedGeometry()
        {
            if (!_maximized || _comp.Views.Count == 0)
            {
                return;
            }

            var view = _comp.Views.FirstOrDefault(v => _comp._layout.OutputAt(X + 1, Y + 1) == v.Output)
                ?? _comp.Views[0];
            ApplyMaximizeGeometry(view);
        }

        public void Destroy()
        {
            if (!SceneSurface.IsDestroyed)
            {
                SceneSurface.Destroy();
            }

            DisposeFrame();
            Tree.Destroy();
            _iconBuffer?.Destroy();
            _iconBuffer = null;
        }
    }
}
