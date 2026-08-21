using Basin.Scene;
using Basin.Shell.River.Protocol;
using Basin.Shell.Xdg;
using Basin.XWayland;

namespace Basin.Shell.River;

internal sealed class RiverWindow
{
    private readonly RiverWindowManager _manager;

    private WmScheduled _scheduled;
    private WmSent _sent;
    private readonly WmRequested _requested = new();
    private readonly RenderRequested _render = new();

    private RiverNode? _node;
    private RiverBorders? _borders;
    private SceneSnapshot? _snapshot;
    private bool _sentIdentity;
    private AppliedState _applied;
    private bool _appliedValid;

    private readonly record struct AppliedState(
        Size Size,
        Size Bounds,
        ResizeEdges Tiled,
        bool Maximized,
        bool Fullscreen,
        bool Resizing,
        bool Activated,
        bool Ssd);

    internal RiverWindow(
        RiverWindowManager manager, XdgToplevelWindow toplevel, SceneSurface scene, SceneTree tree, SceneTree popupTree)
    {
        _manager = manager;
        Toplevel = toplevel;
        Scene = scene;
        Tree = tree;
        PopupTree = popupTree;
        WireToplevel(toplevel);
    }

    internal RiverWindow(
        RiverWindowManager manager, XWaylandWindow xwindow, SceneSurface scene, SceneTree tree, SceneTree popupTree)
    {
        _manager = manager;
        XWindow = xwindow;
        Scene = scene;
        Tree = tree;
        PopupTree = popupTree;
        WireXWindow(xwindow);
    }

    internal XdgToplevelWindow? Toplevel { get; }

    internal XWaylandWindow? XWindow { get; }

    internal SceneSurface Scene { get; }

    internal SceneTree Tree { get; }

    internal SceneTree PopupTree { get; }

    internal List<SceneSurface> PopupScenes { get; } = [];

    internal RiverWindowV1Resource? Resource { get; set; }

    internal WindowPhase Phase { get; private set; } = WindowPhase.Init;

    internal RiverNode? Node => _node;

    internal Size Dimensions { get; private set; }

    internal WmRequested Requested => _requested;

    internal RenderRequested Render => _render;

    internal Surface Surface => Toplevel?.Surface ?? XWindow!.Surface!;

    internal bool IsDisplayable => Phase is WindowPhase.Mapped;

    internal bool IsFrozen => _snapshot is not null;

    internal void Bind(RiverWindowV1Resource resource)
    {
        Resource = resource;
        _sent = default;
        _sentIdentity = false;
        Phase = WindowPhase.Ready;
        WireRequests(resource);
    }

    internal void SendChanges(uint version)
    {
        if (Resource is not { IsDestroyed: false } resource)
        {
            return;
        }

        if (!_sentIdentity)
        {
            _sentIdentity = true;
            if (version >= 2 && _scheduled.Pid is { } pid)
            {
                resource.SendUnreliablePid(pid);
            }

            if (version >= 4 && _scheduled.Identifier is { } identifier)
            {
                resource.SendIdentifier(identifier);
            }
        }

        if (_scheduled.AppId != _sent.AppId || !_sent.AppIdSent)
        {
            resource.SendAppId(_scheduled.AppId);
            _sent.AppId = _scheduled.AppId;
            _sent.AppIdSent = true;
        }

        if (_scheduled.Title != _sent.Title || !_sent.TitleSent)
        {
            resource.SendTitle(_scheduled.Title);
            _sent.Title = _scheduled.Title;
            _sent.TitleSent = true;
        }

        if (!ReferenceEquals(_scheduled.Parent, _sent.Parent) || !_sent.ParentSent)
        {
            resource.SendParent(_scheduled.Parent?.Resource);
            _sent.Parent = _scheduled.Parent;
            _sent.ParentSent = true;
        }

        if (_scheduled.SizeHint != _sent.SizeHint || !_sent.SizeHintSent)
        {
            var hint = _scheduled.SizeHint;
            resource.SendDimensionsHint(hint.MinWidth, hint.MinHeight, hint.MaxWidth, hint.MaxHeight);
            _sent.SizeHint = hint;
            _sent.SizeHintSent = true;
        }

        if (_scheduled.DecorationHint != _sent.DecorationHint || !_sent.DecorationHintSent)
        {
            resource.SendDecorationHint(_scheduled.DecorationHint);
            _sent.DecorationHint = _scheduled.DecorationHint;
            _sent.DecorationHintSent = true;
        }

        if (version >= 5 && (_scheduled.CaptureSessions != _sent.CaptureSessions || !_sent.CaptureSessionsSent))
        {
            resource.SendCaptureSessions(_scheduled.CaptureSessions);
            _sent.CaptureSessions = _scheduled.CaptureSessions;
            _sent.CaptureSessionsSent = true;
        }

        FlushRequestEvents(resource);
    }

    private bool NeedsDimensions()
    {
        if (_resendDimensions || !_sent.DimensionsSent)
        {
            return true;
        }

        var size = CurrentSize();
        return size.Width > 0 && size.Height > 0 && _sent.Dimensions != size;
    }

    internal bool SendDimensionsIfChanged()
    {
        if (Resource is not { IsDestroyed: false } resource)
        {
            return false;
        }

        var size = CurrentSize();
        if (size.Width <= 0 || size.Height <= 0)
        {
            return false;
        }

        if (_sent.DimensionsSent && _sent.Dimensions == size && !_resendDimensions)
        {
            return false;
        }

        _resendDimensions = false;
        Dimensions = size;
        _sent.Dimensions = size;
        _sent.DimensionsSent = true;
        resource.SendDimensions(size.Width, size.Height);
        return true;
    }

    private bool _resendDimensions;

    internal void ForceDimensionResend() => _resendDimensions = true;

    internal void ApplyWindowingState(Transaction transaction, bool focused)
    {
        if (Phase is WindowPhase.Closing)
        {
            return;
        }

        if (_requested.Closed)
        {
            _requested.Closed = false;
            Toplevel?.Close();
            XWindow?.Close();
        }

        var target = _requested.Fullscreen is { } output
            ? new Size(Math.Max(1, output.Width), Math.Max(1, output.Height))
            : _requested.ProposedSize;

        var applied = new AppliedState(
            target,
            _requested.MaxBounds,
            _requested.TiledEdges,
            _requested.InformMaximized,
            _requested.InformFullscreen,
            _requested.InformResizing,
            focused,
            _requested.ServerSideDecorations);
        var changed = !_appliedValid || !_applied.Equals(applied);
        _applied = applied;
        _appliedValid = true;

        if (Toplevel is { } toplevel)
        {
            toplevel.SetActivated(focused);
            toplevel.SetTiled(_requested.TiledEdges);
            toplevel.SetMaximized(_requested.InformMaximized);
            toplevel.SetFullscreen(_requested.InformFullscreen);
            toplevel.SetResizing(_requested.InformResizing);
            toplevel.SetBounds(_requested.MaxBounds.Width, _requested.MaxBounds.Height);

            _manager.Decorations?.SetMode(
                toplevel,
                _requested.ServerSideDecorations ? DecorationMode.ServerSide : DecorationMode.ClientSide);

            if (target.Width > 0 || target.Height > 0)
            {
                toplevel.SetSize(target.Width, target.Height);
            }

            if (changed)
            {
                CaptureSnapshot();
                toplevel.SendConfigure(transaction);

                Scene.SendFrameDone(0);
            }
        }
        else if (XWindow is { } xwindow)
        {
            var position = _node?.RequestedPosition ?? new Point(xwindow.X, xwindow.Y);
            var width = target.Width > 0 ? target.Width : xwindow.Width;
            var height = target.Height > 0 ? target.Height : xwindow.Height;
            if (changed || position.X != xwindow.X || position.Y != xwindow.Y)
            {
                CaptureSnapshot();
                xwindow.Configure(transaction, position.X, position.Y, width, height);
                Scene.SendFrameDone(0);
            }
        }

        if (Phase is WindowPhase.Ready && (_requested.HasProposal || _requested.Fullscreen is not null))
        {
            Phase = WindowPhase.Initialized;
        }
    }

    internal void ApplyRenderState()
    {
        if (Phase is WindowPhase.Initialized && _sent.DimensionsSent)
        {
            Phase = WindowPhase.Mapped;
        }

        var visible = IsDisplayable && !_render.Hidden;
        Tree.Enabled = visible;
        PopupTree.Enabled = visible;
        if (!visible)
        {
            _borders?.Layout(ResizeEdges.None, 0, default, default, visible: false);
            return;
        }

        if (_requested.Fullscreen is { } output)
        {
            Tree.SetPosition(output.Position.X, output.Position.Y);
            PopupTree.SetPosition(output.Position.X, output.Position.Y);
            XWindow?.Configure(output.Position.X, output.Position.Y, output.Width, output.Height);
        }
        else if (_node?.RequestedPosition is { } position)
        {
            Tree.SetPosition(position.X, position.Y);
            PopupTree.SetPosition(position.X, position.Y);
            XWindow?.Configure(position.X, position.Y, XWindow.Width, XWindow.Height);
        }

        var fullscreen = _requested.Fullscreen is not null;
        Tree.ClipBox = fullscreen ? default : _render.ClipBox;

        var content = new Box(0, 0, Dimensions.Width, Dimensions.Height);
        if (!fullscreen && !_render.ContentClipBox.IsEmpty)
        {
            content = content.Intersect(_render.ContentClipBox);
            var geometry = Toplevel is { } toplevel ? toplevel.Xdg.EffectiveGeometry : default;
            Scene.Tree.ClipBox = _render.ContentClipBox.Translated(geometry.X, geometry.Y);
        }
        else
        {
            Scene.Tree.ClipBox = default;
        }

        _borders ??= new RiverBorders(Tree);
        _borders.Layout(
            _render.BorderEdges,
            _render.BorderWidth,
            RiverBorders.ToRenderColor(
                _render.BorderColor.R, _render.BorderColor.G, _render.BorderColor.B, _render.BorderColor.A),
            content,
            visible: !fullscreen);

        _borders.Tree.RaiseToTop();
    }

    internal void SetFullscreenDisplayed(bool displayed, in Box outputBox)
    {
        Tree.Enabled = displayed && !_render.Hidden;
        PopupTree.Enabled = Tree.Enabled;
        if (!displayed)
        {
            return;
        }

        Tree.ClipBox = new Box(0, 0, outputBox.Width, outputBox.Height);
    }

    private void ApplyContentOffset(XdgToplevelWindow toplevel)
    {
        var geometry = toplevel.Xdg.EffectiveGeometry;
        Scene.Tree.SetPosition(-geometry.X, -geometry.Y);
    }

    internal void ReportCaptureGeometry(XdgToplevelSource? source)
    {
        if (source is null || Toplevel is not { } toplevel)
        {
            return;
        }

        var box = IsDisplayable && !_render.Hidden
            ? new Box(Tree.X, Tree.Y, Dimensions.Width, Dimensions.Height)
            : default;
        source.SetGeometry(toplevel, box);
    }

    internal void DestroyPopups()
    {
        foreach (var scene in PopupScenes.ToArray())
        {
            scene.Destroy();
        }

        PopupScenes.Clear();
        PopupTree.Destroy();
    }

    private void CaptureSnapshot()
    {
        if (!IsDisplayable || _snapshot is not null || Tree.Parent is null)
        {
            return;
        }

        _snapshot = SceneSnapshot.Capture(Tree, Tree.Parent);
        Tree.Enabled = false;
    }

    internal void ReleaseSnapshot(ICompositorEventLoop loop)
    {
        if (_snapshot is not { } snapshot)
        {
            return;
        }

        _snapshot = null;
        loop.DeferDestroy(snapshot);
    }

    internal RiverNode EnsureNode(RiverNodeV1Resource resource) =>
        _node ??= new RiverNode(_manager, resource, () => Tree);

    internal bool HasNode => _node is not null;

    internal void BeginClosing()
    {
        if (Phase is WindowPhase.Closing)
        {
            return;
        }

        _borders?.Destroy();
        _borders = null;
        Phase = WindowPhase.Closing;
        _requested.Reset();
        _render.Reset();
    }

    internal void ResetForNewManager()
    {
        Resource = null;
        _sent = default;
        _sentIdentity = false;
        _requested.Reset();
        _render.Reset();
        _node = null;
        _appliedValid = false;
        if (Phase is not WindowPhase.Closing)
        {
            Phase = WindowPhase.Init;
        }
    }

    internal void ScheduleIdentity(int pid, string? identifier)
    {
        _scheduled.Pid = pid;
        _scheduled.Identifier = identifier;
    }

    internal void ScheduleCaptureSessions(uint count) => _scheduled.CaptureSessions = count;

    internal void SchedulePresentationHint(RiverOutputV1.PresentationMode hint)
    {
        _scheduled.PresentationHint = hint;
        _hasPresentationHint = true;
    }

    private bool _hasPresentationHint;

    internal void SendPresentationHintIfChanged(uint version)
    {
        if (version < 4 || !_hasPresentationHint || Resource is not { IsDestroyed: false } resource)
        {
            return;
        }

        if (_sent.PresentationHintSent && _sent.PresentationHint == _scheduled.PresentationHint)
        {
            return;
        }

        _sent.PresentationHint = _scheduled.PresentationHint;
        _sent.PresentationHintSent = true;
        resource.SendPresentationHint(_scheduled.PresentationHint);
    }

    internal void ScheduleDecorationHint(RiverWindowV1.DecorationHint hint)
    {
        _scheduled.DecorationHint = hint;
        _manager.MarkManageDirty();
    }

    internal void ScheduleParent(RiverWindow? parent) => _scheduled.Parent = parent;

    private Size CurrentSize()
    {
        if (Toplevel is { } toplevel)
        {
            var geometry = toplevel.Xdg.EffectiveGeometry;
            return new Size(geometry.Width, geometry.Height);
        }

        return XWindow is { } xwindow ? new Size(xwindow.Width, xwindow.Height) : default;
    }

    private void WireToplevel(XdgToplevelWindow toplevel)
    {
        _scheduled.DecorationHint = _manager.DecorationHintFor(toplevel);
        _scheduled.AppId = Empty(toplevel.AppId);
        _scheduled.Title = Empty(toplevel.Title);
        RefreshSizeHint(toplevel);
        ApplyContentOffset(toplevel);

        toplevel.AppIdChanged += () =>
        {
            _scheduled.AppId = Empty(toplevel.AppId);
            _manager.MarkManageDirty();
        };
        toplevel.TitleChanged += () =>
        {
            _scheduled.Title = Empty(toplevel.Title);
            _manager.MarkManageDirty();
        };
        toplevel.Xdg.Committed += () =>
        {
            var hint = _scheduled.SizeHint;
            RefreshSizeHint(toplevel);
            ApplyContentOffset(toplevel);
            if (_scheduled.SizeHint != hint || NeedsDimensions())
            {
                _manager.MarkRenderDirty();
            }
        };
        toplevel.MaximizeRequested += maximized => Request(
            maximized ? PendingRequest.Maximize : PendingRequest.Unmaximize);
        toplevel.FullscreenRequested += fullscreen => Request(
            fullscreen ? PendingRequest.Fullscreen : PendingRequest.ExitFullscreen);
        toplevel.MinimizeRequested += () => Request(PendingRequest.Minimize);
        toplevel.ShowWindowMenuRequested += (x, y) =>
        {
            _windowMenuAt = new Point(x, y);
            Request(PendingRequest.WindowMenu);
        };
        toplevel.MoveRequested += _ => Request(PendingRequest.PointerMove);
        toplevel.ResizeRequested += (_, edges) =>
        {
            _resizeEdges = edges;
            Request(PendingRequest.PointerResize);
        };

        if (toplevel.RequestedMaximized is { } maximized)
        {
            Request(maximized ? PendingRequest.Maximize : PendingRequest.Unmaximize);
        }

        if (toplevel.RequestedFullscreen is { } fullscreen)
        {
            Request(fullscreen ? PendingRequest.Fullscreen : PendingRequest.ExitFullscreen);
        }

        if (toplevel.RequestedMinimized)
        {
            Request(PendingRequest.Minimize);
        }
    }

    private void WireXWindow(XWaylandWindow xwindow)
    {
        _scheduled.DecorationHint = xwindow.WantsDecorations
            ? RiverWindowV1.DecorationHint.PrefersSsd
            : RiverWindowV1.DecorationHint.OnlySupportsCsd;

        _scheduled.AppId = NullIfEmpty(xwindow.Class);
        _scheduled.Title = NullIfEmpty(xwindow.Title);

        xwindow.TitleChanged += () =>
        {
            _scheduled.Title = NullIfEmpty(xwindow.Title);
            _manager.MarkManageDirty();
        };
        xwindow.GeometryChanged += () => _manager.MarkRenderDirty();
    }

    private void RefreshSizeHint(XdgToplevelWindow toplevel) =>
        _scheduled.SizeHint = new SizeHint(
            toplevel.MinWidth, toplevel.MinHeight, toplevel.MaxWidth, toplevel.MaxHeight);

    private void Request(PendingRequest request)
    {
        _pending |= request;
        _manager.MarkManageDirty();
    }

    private PendingRequest _pending;
    private Point _windowMenuAt;
    private ResizeEdges _resizeEdges;

    private void FlushRequestEvents(RiverWindowV1Resource resource)
    {
        if (_pending == PendingRequest.None)
        {
            return;
        }

        var pending = _pending;
        _pending = PendingRequest.None;

        if ((pending & PendingRequest.Maximize) != 0)
        {
            resource.SendMaximizeRequested();
        }

        if ((pending & PendingRequest.Unmaximize) != 0)
        {
            resource.SendUnmaximizeRequested();
        }

        if ((pending & PendingRequest.Minimize) != 0)
        {
            resource.SendMinimizeRequested();
        }

        if ((pending & PendingRequest.Fullscreen) != 0)
        {
            resource.SendFullscreenRequested(_manager.OutputUnderPointer()?.Resource);
        }

        if ((pending & PendingRequest.ExitFullscreen) != 0)
        {
            resource.SendExitFullscreenRequested();
        }

        if ((pending & PendingRequest.WindowMenu) != 0)
        {
            resource.SendShowWindowMenuRequested(_windowMenuAt.X, _windowMenuAt.Y);
        }

        if ((pending & PendingRequest.PointerMove) != 0)
        {
            if (_manager.PrimarySeat?.Resource is { } moveSeat)
            {
                resource.SendPointerMoveRequested(moveSeat);
            }
            else
            {
                _pending |= PendingRequest.PointerMove;
            }
        }

        if ((pending & PendingRequest.PointerResize) != 0)
        {
            if (_manager.PrimarySeat?.Resource is { } resizeSeat)
            {
                resource.SendPointerResizeRequested(resizeSeat, ToRiverEdges(_resizeEdges));
            }
            else
            {
                _pending |= PendingRequest.PointerResize;
            }
        }
    }

    private static RiverWindowV1.Edges ToRiverEdges(ResizeEdges edges)
    {
        var result = RiverWindowV1.Edges.None;
        if ((edges & ResizeEdges.Top) != 0)
        {
            result |= RiverWindowV1.Edges.Top;
        }

        if ((edges & ResizeEdges.Bottom) != 0)
        {
            result |= RiverWindowV1.Edges.Bottom;
        }

        if ((edges & ResizeEdges.Left) != 0)
        {
            result |= RiverWindowV1.Edges.Left;
        }

        if ((edges & ResizeEdges.Right) != 0)
        {
            result |= RiverWindowV1.Edges.Right;
        }

        return result;
    }

    private static string? Empty(string value) => value;

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private void WireRequests(RiverWindowV1Resource resource)
    {
        resource.ProposeDimensions += (_, e) =>
        {
            if (!_manager.EnsureWindowing())
            {
                return;
            }

            if (e.Width < 0 || e.Height < 0)
            {
                resource.PostError((uint)RiverWindowV1.Error.InvalidDimensions, "dimensions must be non-negative");
                return;
            }

            _requested.ProposedSize = new Size(e.Width, e.Height);
            _requested.HasProposal = true;

            ForceDimensionResend();
        };
        resource.SetDimensionBounds += (_, e) =>
        {
            if (!_manager.EnsureWindowing())
            {
                return;
            }

            if (e.MaxWidth < 0 || e.MaxHeight < 0)
            {
                resource.PostError((uint)RiverWindowV1.Error.InvalidDimensions, "bounds must be non-negative");
                return;
            }

            _requested.MaxBounds = new Size(e.MaxWidth, e.MaxHeight);
        };
        resource.Close += (_, _) =>
        {
            if (_manager.EnsureWindowing())
            {
                _requested.Closed = true;
            }
        };
        resource.UseCsd += (_, _) =>
        {
            if (_manager.EnsureWindowing())
            {
                _requested.ServerSideDecorations = false;
            }
        };
        resource.UseSsd += (_, _) =>
        {
            if (_manager.EnsureWindowing())
            {
                _requested.ServerSideDecorations = true;
            }
        };
        resource.SetTiled += (_, e) =>
        {
            if (_manager.EnsureWindowing())
            {
                _requested.TiledEdges = FromRiverEdges(e.Edges);
            }
        };
        resource.SetCapabilities += (_, e) =>
        {
            if (_manager.EnsureWindowing())
            {
                _requested.Capabilities = e.Caps;
            }
        };
        resource.InformMaximized += (_, _) => SetIfWindowing(() => _requested.InformMaximized = true);
        resource.InformUnmaximized += (_, _) => SetIfWindowing(() => _requested.InformMaximized = false);
        resource.InformFullscreen += (_, _) => SetIfWindowing(() => _requested.InformFullscreen = true);
        resource.InformNotFullscreen += (_, _) => SetIfWindowing(() => _requested.InformFullscreen = false);
        resource.InformResizeStart += (_, _) => SetIfWindowing(() => _requested.InformResizing = true);
        resource.InformResizeEnd += (_, _) => SetIfWindowing(() => _requested.InformResizing = false);
        resource.Fullscreen += (_, e) =>
        {
            if (_manager.EnsureWindowing())
            {
                var output = _manager.ResolveOutput(e.Output);
                if (output is { IsRemoved: true })
                {
                    return;
                }

                _requested.Fullscreen = output;

                ForceDimensionResend();
            }
        };
        resource.ExitFullscreen += (_, _) => SetIfWindowing(() =>
        {
            _requested.Fullscreen = null;
            ForceDimensionResend();
        });

        resource.Show += (_, _) => SetIfRendering(() => _render.Hidden = false);
        resource.Hide += (_, _) => SetIfRendering(() => _render.Hidden = true);
        resource.SetBorders += (_, e) =>
        {
            if (!_manager.EnsureRendering())
            {
                return;
            }

            if (e.Width < 0)
            {
                resource.PostError((uint)RiverWindowV1.Error.InvalidBorder, "border width must be non-negative");
                return;
            }

            _render.BorderEdges = FromRiverEdges(e.Edges);
            _render.BorderWidth = e.Width;
            _render.BorderColor = (e.R, e.G, e.B, e.A);
        };
        resource.SetClipBox += (_, e) =>
        {
            if (!_manager.EnsureRendering())
            {
                return;
            }

            if (e.Width < 0 || e.Height < 0)
            {
                resource.PostError((uint)RiverWindowV1.Error.InvalidClipBox, "clip box size must be non-negative");
                return;
            }

            _render.ClipBox = new Box(e.X, e.Y, e.Width, e.Height);
        };
        resource.SetContentClipBox += (_, e) =>
        {
            if (!_manager.EnsureRendering())
            {
                return;
            }

            if (e.Width < 0 || e.Height < 0)
            {
                resource.PostError((uint)RiverWindowV1.Error.InvalidClipBox, "clip box size must be non-negative");
                return;
            }

            _render.ContentClipBox = new Box(e.X, e.Y, e.Width, e.Height);
        };

        resource.GetDecorationAbove += (_, e) => CreateDecoration(resource, e.Id, e.Surface, above: true);
        resource.GetDecorationBelow += (_, e) => CreateDecoration(resource, e.Id, e.Surface, above: false);

        resource.GetNode += (_, e) =>
        {
            if (HasNode)
            {
                resource.PostError((uint)RiverWindowV1.Error.NodeExists, "the window already has a node");
                return;
            }

            var node = new RiverNodeV1Resource(resource.Client, resource.Version, e.Id);
            _manager.RegisterNode(EnsureNode(node));
        };
        resource.DestroyRequest += (_, _) => _manager.ForgetWindowResource(this);
    }

    private void CreateDecoration(RiverWindowV1Resource resource, uint id, Wayland.WlSurfaceResource? surfaceResource, bool above)
    {
        if (_manager.Compositor is null || surfaceResource is null)
        {
            return;
        }

        var surface = _manager.Compositor.ResolveSurface(surfaceResource);
        if (surface is null || !surface.CanSetRole(RiverDecoration.RoleName) ||
            surface.Current.Buffer is not null || surface.Pending.Buffer is not null)
        {
            resource.PostError(
                (uint)Protocol.RiverWindowManagerV1.Error.Role,
                "the surface already has a role or a buffer");
            return;
        }

        var decorationResource = new RiverDecorationV1Resource(resource.Client, resource.Version, id);
        var decoration = new RiverDecoration(_manager, decorationResource, this, surface, Tree, above);
        surface.TrySetRole(RiverDecoration.RoleName, decoration);
        _manager.AddDecoration(decoration);
    }

    private void SetIfWindowing(Action apply)
    {
        if (_manager.EnsureWindowing())
        {
            apply();
        }
    }

    private void SetIfRendering(Action apply)
    {
        if (_manager.EnsureRendering())
        {
            apply();
        }
    }

    private static ResizeEdges FromRiverEdges(RiverWindowV1.Edges edges)
    {
        var result = ResizeEdges.None;
        if ((edges & RiverWindowV1.Edges.Top) != 0)
        {
            result |= ResizeEdges.Top;
        }

        if ((edges & RiverWindowV1.Edges.Bottom) != 0)
        {
            result |= ResizeEdges.Bottom;
        }

        if ((edges & RiverWindowV1.Edges.Left) != 0)
        {
            result |= ResizeEdges.Left;
        }

        if ((edges & RiverWindowV1.Edges.Right) != 0)
        {
            result |= ResizeEdges.Right;
        }

        return result;
    }

    [Flags]
    private enum PendingRequest
    {
        None = 0,
        Maximize = 1,
        Unmaximize = 2,
        Minimize = 4,
        Fullscreen = 8,
        ExitFullscreen = 16,
        WindowMenu = 32,
        PointerMove = 64,
        PointerResize = 128,
    }

    private struct WmScheduled
    {
        public string? AppId;
        public string? Title;
        public RiverWindow? Parent;
        public SizeHint SizeHint;
        public RiverWindowV1.DecorationHint DecorationHint;
        public uint CaptureSessions;
        public RiverOutputV1.PresentationMode PresentationHint;
        public int? Pid;
        public string? Identifier;
    }

    private struct WmSent
    {
        public string? AppId;
        public bool AppIdSent;
        public string? Title;
        public bool TitleSent;
        public RiverWindow? Parent;
        public bool ParentSent;
        public SizeHint SizeHint;
        public bool SizeHintSent;
        public RiverWindowV1.DecorationHint DecorationHint;
        public bool DecorationHintSent;
        public uint CaptureSessions;
        public bool CaptureSessionsSent;
        public Size Dimensions;
        public bool DimensionsSent;
        public RiverOutputV1.PresentationMode PresentationHint;
        public bool PresentationHintSent;
    }

    internal sealed class WmRequested
    {
        public Size ProposedSize;
        public bool HasProposal;
        public Size MaxBounds;
        public bool Closed;
        public bool ServerSideDecorations;
        public ResizeEdges TiledEdges;
        public RiverWindowV1.Capabilities Capabilities = RiverWindowV1.Capabilities.WindowMenu
            | RiverWindowV1.Capabilities.Maximize
            | RiverWindowV1.Capabilities.Fullscreen
            | RiverWindowV1.Capabilities.Minimize;
        public bool InformMaximized;
        public bool InformFullscreen;
        public bool InformResizing;
        public RiverOutput? Fullscreen;

        public void Reset()
        {
            ProposedSize = default;
            HasProposal = false;
            MaxBounds = default;
            Closed = false;
            ServerSideDecorations = false;
            TiledEdges = ResizeEdges.None;
            Capabilities = RiverWindowV1.Capabilities.WindowMenu
                | RiverWindowV1.Capabilities.Maximize
                | RiverWindowV1.Capabilities.Fullscreen
                | RiverWindowV1.Capabilities.Minimize;
            InformMaximized = false;
            InformFullscreen = false;
            InformResizing = false;
            Fullscreen = null;
        }
    }

    internal sealed class RenderRequested
    {
        public bool Hidden;
        public ResizeEdges BorderEdges;
        public int BorderWidth;
        public (uint R, uint G, uint B, uint A) BorderColor;
        public Box ClipBox;
        public Box ContentClipBox;

        public void Reset()
        {
            Hidden = false;
            BorderEdges = ResizeEdges.None;
            BorderWidth = 0;
            BorderColor = default;
            ClipBox = default;
            ContentClipBox = default;
        }
    }

    internal readonly record struct SizeHint(int MinWidth, int MinHeight, int MaxWidth, int MaxHeight);
}
