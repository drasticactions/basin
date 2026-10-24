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

        public bool Maximized => _maximized;
        private RestoreGeometry _restore;

        public XWindow(TinyComp comp, Basin.XWayland.XWaylandWindow xwin, SceneSurface scene, bool framable)
        {
            _comp = comp;
            XWin = xwin;
            Framable = framable;
            Rule = framable ? comp._config.RuleFor(FactsOf(xwin)) : null;
            CornerRadius = Rule?.CornerRadius ?? comp._cornerRadius;
            Tree = comp._xSceneDriver.ParentOf(xwin) ?? scene.Tree.Parent!;
            Tree.SetPosition(xwin.X, xwin.Y);
            SceneSurface = scene;
            Outer = comp._xSceneDriver.OuterOf(xwin) ?? scene.Tree;
            Outer.SetPosition(0, 0);
            comp.RefreshSurfaceLuts();
            ApplyContentShader();
            RefreshShadow();
            ApplyQuirks();
            xwin.PropertiesChanged += ApplyQuirks;
            xwin.PropertiesChanged += RefreshScheme;
            xwin.TitleChanged += Layout;
            xwin.IconChanged += RefreshIcon;
            UpdateDecorations();
            RefreshIcon();
        }

        public Rule? Rule { get; }

        public SceneTree Outer { get; }

        public Basin.Config.WindowQuirks Quirks { get; private set; } = Basin.Config.WindowQuirks.None;

        private string _shownVariant = string.Empty;

        private void RefreshScheme()
        {
            if (_frame is null || SceneSurface.IsDestroyed || XWin.ThemeVariant == _shownVariant)
            {
                return;
            }

            _shownVariant = XWin.ThemeVariant;
            Layout();
        }

        private void ApplyQuirks()
        {
            if (!Framable || SceneSurface.IsDestroyed)
            {
                return;
            }

            var quirks = Basin.Config.WindowRule.QuirksFor(_comp._config.Rules, FactsOf(XWin));
            Quirks = quirks;
            XWin.Letterbox = quirks.Letterbox switch
            {
                Basin.Config.LetterboxQuirk.Fit => SceneFitPolicy.Fit,
                Basin.Config.LetterboxQuirk.Center => SceneFitPolicy.Center,
                Basin.Config.LetterboxQuirk.Off => SceneFitPolicy.Off,
                Basin.Config.LetterboxQuirk.Integer => SceneFitPolicy.Integer,
                Basin.Config.LetterboxQuirk.Fill => SceneFitPolicy.Fill,
                Basin.Config.LetterboxQuirk.Stretch => SceneFitPolicy.Stretch,
                _ => SceneFitPolicy.Auto,
            };
            XWin.Readiness = quirks.Readiness == Basin.Config.ReadinessQuirk.Immediate
                ? Basin.XWayland.XWaylandReadiness.Immediate
                : Basin.XWayland.XWaylandReadiness.OnMatchingCommit;
            XWin.ForceUnmaximize = quirks.ForceUnmaximize ?? false;
            XWin.AllowClientMove = quirks.AllowClientMove ?? false;
            XWin.HonorEmulatedModes = quirks.EmulatedModes ?? true;
            Outer.Filter = quirks.ScaleFilter;
            if (quirks.FrameLimit is { } limit && XWin.Surface is { } surface)
            {
                _comp._services.Find<Basin.Capabilities.IFrameLimits>()?.SetLimit(surface, limit);
            }

            _comp._xSceneDriver.Refit(XWin);
        }

        public static Basin.Config.WindowFacts FactsOf(Basin.XWayland.XWaylandWindow xwin) =>
            Basin.Config.WindowFacts.ForX11(xwin.Class, xwin.Instance, xwin.Title, xwin.SteamGame, xwin.WindowType, xwin.Process) with
            {
                WantsFocus = xwin.WantsFocus,
                AcceptsInput = xwin.AcceptsInput,
                TakesFocus = xwin.TakesFocus,
                SkipTaskbar = xwin.SkipTaskbar,
                SkipPager = xwin.SkipPager,
                HwndStyle = xwin.HwndStyle,
                HwndStyleEx = xwin.HwndStyleEx,
                IsUseless = xwin.IsUseless,
            };

        public int CornerRadius { get; }

        internal Basin.CornerClip ContentClip =>
            _cornerRig is { } rig ? rig.ContentClip : new Basin.CornerClip(ClientCornerRadius);

        private void ApplyContentShader()
        {
            if (SceneSurface.IsDestroyed)
            {
                return;
            }

            SceneSurface.Content.Corners = ContentClip;
        }

        public void ApplyDim(float brightness, float saturation)
        {
            if (SceneSurface.IsDestroyed)
            {
                return;
            }

            SceneSurface.Content.Brightness = brightness;
            SceneSurface.Content.Saturation = saturation;
        }

        public void RefreshShadow()
        {
            var (shadow, glow) = _comp.ShadowsFor(Rule?.FrameStyle ?? _comp._frameStyle, _frame is not null);
            SetShadow(shadow, glow);
        }

        public void SetShadow(Basin.Effects.DropShadowTexture? texture, Basin.Effects.DropShadowTexture? glow = null)
        {
            if ((texture is null && glow is null) || !Framable)
            {
                _shadow?.Dispose();
                _shadow = null;
                return;
            }

            _shadow ??= new Basin.Effects.DropShadowEffect(Tree);
            _shadow.Texture = texture;
            _shadow.GlowTexture = glow;
            _shadow.GlowOpacity = glow is null ? 0 : _glowEmphasis;
            LayoutShadow();
        }

        internal void LayoutShadow() =>
            _shadow?.SetGeometry(_frame is { Visible: true } frame && !frame.OuterBounds.IsEmpty
                ? frame.OuterBounds
                : new Box(0, 0, Math.Max(Shown.Width, 1), Math.Max(Shown.Height, 1)));

        private void OnShapePublished(FrameBackdropShape shape)
        {
            _glowEmphasis = shape.Emphasis;
            if (_shadow is { GlowTexture: not null } shadow)
            {
                shadow.GlowOpacity = shape.Emphasis;
            }
        }

        private double _glowEmphasis = 1;

        private int _glassContentRadius = -1;

        public int ClientCornerRadius => _frame is not null && _glassContentRadius >= 0 ? _glassContentRadius : CornerRadius;

        internal FrameCornerRig? DetachCornerRig()
        {
            var rig = _cornerRig;
            _cornerRig = null;
            return rig;
        }

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

        public int X => XWin.X + OffsetX;

        public int Y => XWin.Y + OffsetY;

        public int OffsetX { get; private set; }

        public int OffsetY { get; private set; }

        public (int Width, int Height) GeometrySize => (XWin.Width, XWin.Height);

        public void MoveTo(int x, int y) => ResizeTo(x, y, XWin.Width, XWin.Height, ResizeEdges.None);

        private Box Shown => _shown ?? new Box(X, Y, XWin.Width, XWin.Height);

        private Box? _shown;
        private Transaction? _resizeFlow;
        private Box? _heldResize;
        private bool _resizing;

        public void ResizeTo(int x, int y, int width, int height, ResizeEdges edges)
        {
            _comp._effects.OnResized(
                Tree,
                new Box(0, 0, Math.Max(width, 1), Math.Max(height, 1)),
                new Box(0, 0, Math.Max(XWin.Width, 1), Math.Max(XWin.Height, 1)),
                new Box(0, 0, Math.Max(width, 1), Math.Max(height, 1)),
                0,
                0);
            var box = new Box(x, y, width, height);
            if (_resizing && _comp._useTransactions)
            {
                if (_resizeFlow is not null)
                {
                    _heldResize = box;
                }
                else if (box != new Box(X, Y, XWin.Width, XWin.Height))
                {
                    SendPacedResize(box);
                }

                return;
            }

            DropResizeFlow();
            Place(box);
            XWin.Configure(box.X - OffsetX, box.Y - OffsetY, width, height);
            Layout();
        }

        private void Place(Box box)
        {
            var root = _comp.XRoot();
            var sideX = Inside(box.X, box.Width, root.X, root.Right);
            var sideY = Inside(box.Y, box.Height, root.Y, root.Bottom);
            OffsetX = box.X - sideX;
            OffsetY = box.Y - sideY;
        }

        private void SendPacedResize(Box box)
        {
            _shown = Shown;
            Place(box);
            var flow = new Transaction(_comp.Loop);
            _resizeFlow = flow;
            flow.Completed += () => OnPacedResizeLanded(flow);
            XWin.Configure(flow, box.X - OffsetX, box.Y - OffsetY, box.Width, box.Height);
            flow.Seal();
        }

        private void OnPacedResizeLanded(Transaction flow)
        {
            if (!ReferenceEquals(_resizeFlow, flow))
            {
                return;
            }

            _resizeFlow = null;
            flow.Dispose();
            _shown = null;
            if (SceneSurface.IsDestroyed)
            {
                return;
            }

            Layout();
            if (_heldResize is { } held)
            {
                _heldResize = null;
                if (held != new Box(X, Y, XWin.Width, XWin.Height))
                {
                    SendPacedResize(held);
                }
            }
        }

        private void DropResizeFlow()
        {
            _heldResize = null;
            if (_resizeFlow is { } flow)
            {
                _resizeFlow = null;
                flow.Dispose();
            }

            _shown = null;
        }

        private static int Inside(int origin, int size, int low, int high) =>
            origin >= low && origin + size <= high ? origin : Math.Clamp(origin, low, Math.Max(low, high - size));

        public void SetResizing(bool resizing) => _resizing = resizing;

        public void Layout()
        {
            var shown = Shown;
            Tree.SetPosition(shown.X, shown.Y);
            Outer.SetPosition(0, 0);
            LayoutShadow();
            if (Framable)
            {
                _comp.ApplyCanvas(this);
            }

            ReportGeometry();
            _comp._workspaceModel.RaiseMembersChanged();
            if (_frame is not null && shown.Width > 0 && shown.Height > 0)
            {
                _frame.Visible = true;
                _frame.Window = XWin.Surface;
                _frame.Configure(new Box(0, 0, shown.Width, shown.Height), _comp.ScaleAt(shown.X + 1, shown.Y + 1), BuildState());
                _frame.Commit();
                LayoutShadow();
            }
        }

        public void ReportGeometry()
        {
            var shown = Shown;
            var client = new Box(shown.X, shown.Y, Math.Max(shown.Width, 1), Math.Max(shown.Height, 1));
            var frame = _frame is null ? client : FrameBox;
            _comp._xwaylandModule.DisplayOf(XWin)?.Toplevels?.SetGeometry(XWin, frame, client);
        }

        public void SetNoBorderOverride(bool noBorder)
        {
            _noBorderOverride = noBorder;
            if (noBorder)
            {
                DisposeFrame();
                ApplyContentShader();
                ReportDecoration();
                ReportGeometry();
            }
            else
            {
                UpdateDecorations();
            }
        }

        private bool? _noBorderOverride;

        private bool WantsFrame => Framable && !_fullscreen && (_noBorderOverride is { } o ? !o : XWin.WantsDecorations);

        private bool _fullscreen;

        public bool Fullscreen => _fullscreen;

        public void SetFullscreen(bool fullscreen)
        {
            if (fullscreen == _fullscreen)
            {
                XWin.SetFullscreen(fullscreen);
                return;
            }

            if (fullscreen)
            {
                var view = _comp.Views.FirstOrDefault(v => _comp._layout.OutputAt(_comp._cursorX, _comp._cursorY) == v.Output)
                    ?? _comp.Views[0];
                if (!_maximized)
                {
                    _restore = _restore.Saving(new Box(X, Y, XWin.Width, XWin.Height));
                }

                _fullscreen = true;
                XWin.SetFullscreen(true);
                _comp.RefreshDim();
                UpdateDecorations();
                _comp.StraightenForFill(this);
                var box = _comp._layout.BoxOf(view.Output);
                ResizeTo(box.X, box.Y, box.Width, box.Height, ResizeEdges.None);
                return;
            }

            _fullscreen = false;
            XWin.SetFullscreen(false);
            _comp.RefreshDim();
            UpdateDecorations();
            if (_maximized)
            {
                ReapplyPinnedGeometry();
            }
            else if (_restore.TryGet(out var saved))
            {
                _restore = RestoreGeometry.None;
                ResizeTo(saved.X, saved.Y, saved.Width, saved.Height, ResizeEdges.None);
            }
            else
            {
                _comp.PlaceCascade(this);
            }
        }

        private void ReportDecoration() =>
            _comp._xwaylandModule.DisplayOf(XWin)?.Toplevels?.SetDecoration(XWin, noBorder: !WantsFrame, userCanSet: Framable);

        internal void DisposeFrame()
        {
            _cornerRig?.Dispose();
            _cornerRig = null;
            _comp.ForgetFrameBackdrop(_frame);
            if (_frame is not null)
            {
                _comp.AccessibleChrome?.Remove(_frame);
            }

            _frame?.Dispose();
            _frame = null;
            RefreshShadow();
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
                ApplyContentShader();
                ReportGeometry();
                return;
            }

            if (_frame is not null
                || _comp.CreateFrameRenderer(Rule?.FrameStyle ?? _comp._frameStyle) is not { } renderer)
            {
                return;
            }

            _frame = new Frame(_comp.FrameHosts, renderer, Tree)
            {
                Clock = _comp.FrameClock,
                MenuLayer = _comp._layers.Overlay,
                TouchSlop = TouchGripSlop,
            };
            _comp.ApplyFrameBackdrop(_frame, renderer);
            _frame.ShapePublished += OnShapePublished;
            _glassContentRadius = renderer is Basin.Frames.Quill.QuillFrameRenderer { Theme.Glass: true } glassRenderer
                ? (int)Math.Round(glassRenderer.Theme.ResolvedContentRadius)
                : -1;
            _frame.Requested += OnFrameAction;
            _comp.AccessibleChrome?.Add(_frame);
            _frame.Faulted += e => _comp.Log.Error(e, $"frame: faulted for {XWin.Class}");
            Outer.RaiseToTop();
            Layout();
            RefreshShadow();
            if (_glassContentRadius >= 0)
            {
                if (SceneSurface is not null)
                {
                    ApplyContentShader();
                }
            }
            else if (CornerRadius > 0)
            {
                _cornerRig = new FrameCornerRig(_frame, SceneSurface.Content, CornerRadius, ApplyContentShader);
                ApplyContentShader();
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

                var shown = Shown;
                var insets = _frame.Measure(BuildState(), _comp.ScaleAt(shown.X + 1, shown.Y + 1));
                return new Box(
                    shown.X - insets.Left,
                    shown.Y - insets.Top,
                    shown.Width + insets.Left + insets.Right,
                    shown.Height + insets.Top + insets.Bottom);
            }
        }

        private FrameState BuildState() => new()
        {
            Title = XWin.Title,
            AppId = XWin.Class,
            Icon = new FrameIcon(null, _iconBuffer),
            Active = _active,
            Maximized = _maximized,
            Capabilities = FrameCapabilities.WindowMenu | FrameCapabilities.Maximize | FrameCapabilities.Minimize
                | (_comp._densityControl is null || _comp._xwaylandModule.LowDensity is null ? FrameCapabilities.None : FrameCapabilities.X11Density),
            LowDensity = _comp._xwaylandModule.DisplayOf(XWin)?.Density == Basin.X11Density.Low,
            Kind = XWin.TransientFor is null ? FrameKind.Normal : FrameKind.Dialog,
            ColorScheme = XWin.ThemeVariant switch
            {
                "dark" => FrameColorScheme.Dark,
                "light" => FrameColorScheme.Light,
                _ => FrameColorScheme.Default,
            },
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
                case FrameActionKind.ToggleX11Density:
                    _comp.ToggleX11Density(XWin);
                    break;
            }
        }

        public (int Width, int Height)? RestoreForDrag()
        {
            if (!_maximized)
            {
                return null;
            }

            _maximized = false;
            XWin.SetMaximized(false);
            if (_restore.TryGet(out var saved))
            {
                _restore = RestoreGeometry.None;
                ResizeTo(X, Y, saved.Width, saved.Height, ResizeEdges.None);
                return (saved.Width, saved.Height);
            }

            return null;
        }

        public void ToggleMaximize()
        {
            if (_maximized)
            {
                _maximized = false;
                XWin.SetMaximized(false);
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
            _restore = _restore.Saving(new Box(X, Y, XWin.Width, XWin.Height));
            _maximized = true;
            XWin.SetMaximized(true);
            _comp.StraightenForFill(this);
            ApplyMaximizeGeometry(view);
        }

        private void ApplyMaximizeGeometry(OutputView view)
        {
            var box = _comp._layout.BoxOf(view.Output);
            var usable = _comp.FlatArea(view, view.UsableArea.IsEmpty ? box with { X = 0, Y = 0 } : view.UsableArea);
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
            DropResizeFlow();
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
