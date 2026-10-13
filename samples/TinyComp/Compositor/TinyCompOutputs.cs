using System.Diagnostics;
using Basin;
using Basin.Backend.Libinput;
using Basin.Cli;
using Basin.Effects;
using Basin.Backend.Wayland;
using Basin.Host;
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
    private Basin.Render.Vulkan.VulkanDeviceBlitter? BlitterFor(ulong deviceId)
    {
        foreach (var blitter in _blitters)
        {
            if (DrmDevices.TryDeviceId(blitter.DevicePath, out var id) && id == deviceId)
            {
                return blitter;
            }
        }

        return null;
    }

    private RenderStack CreateStack(ref string rendererName, string? renderNodePath) =>
        Basin.Renderers.RendererCatalog.CreateWithFallback(ref rendererName, renderNodePath, ReportFallback);

    private Basin.Capabilities.ImageDescription DescriptionOf(IOutput output) =>
        _colorConfiguration is { } configuration
            ? configuration.DescriptionOf(output)
            : Basin.Capabilities.ImageDescription.SdrDefault;

    private void ReportFallback(Basin.Renderers.RendererFallback fallback) =>
        _log.Warn($"{(fallback.Describe())}");

    private void WireOutputDriver()
    {
        _driver.HostChrome = AttachHostChrome;
        _driver.Arrange = ArrangeOutputs;
        _driver.ConfiguredScale = output => _config.OutputSettingFor(output.Name)?.Scale;
        _driver.Added += OnViewAdded;
        _driver.Removed += OnViewRemoved;
        _driver.Emptied += () => _runLoop.Stop();
        _driver.Painted += OnPainted;
        _driver.ModeChanged += OnModeChanged;
        _driver.LayoutChanged += OnLayoutChanged;
        _driver.TransformChanged += OnTransformChanged;
        _driver.StampModeset += OnStampModeset;
        _driver.ScanoutChanged += OnScanoutChanged;
        _driver.ScaleRefused += (view, scale) => _log.Warn($"scale {scale} refused by {view.Output.Name}");
        _driver.ModesetRefused += card => _log.Error($"modeset refused by {card.Name}");
        _driver.HandoffInherited += inheritance => _report.Line(HandoffLines.Inherit(inheritance));
        _host.HandoffRetained += retention => _report.Line(HandoffLines.Retain(retention));
        _driver.CardAdopted += device => _report.Line($"CARD + {device.CardPath} ({device.Driver})");
        _driver.CardRefused += (device, reason) => _report.Line($"CARD {device.CardPath} not adopted: {reason}");
        _driver.SecondaryOutputAdded += output => _report.Line($"OUTPUT + {output.Name}");
        _driver.HostScaleFollowed += view =>
        {
            _report.Line($"SCALE {view.Output.Name} {view.Output.Scale}");
            RefreshOutputLayout();
        };
    }

    private static bool AutoLayoutOf(OutputView view) =>
        view.Tag is not OutputPolicy policy || policy.AutoLayout;

    private void ArrangeOutputs(IReadOnlyList<OutputView> views)
    {
        var edge = 0;
        var row = 0;
        foreach (var view in views)
        {
            if (AutoLayoutOf(view) || !_layout.Contains(view.Output))
            {
                continue;
            }

            var pinned = _layout.BoxOf(view.Output);
            if (pinned.Right > edge)
            {
                (edge, row) = (pinned.Right, pinned.Y);
            }
        }

        foreach (var view in views)
        {
            if (!AutoLayoutOf(view) || !_layout.Contains(view.Output))
            {
                continue;
            }

            _layout.Move(view.Output, edge, row);
            edge += view.Output.LogicalSize().Width;
        }
    }

    private void OnViewAdded(OutputView view)
    {
        view.Tag = new OutputPolicy();
        _presenceTracker.AddOutput(view.Output, view.Global);
        InitWorkspaces(view);
        ConfigureOverviewTriggers();
        LayoutCanvas(view);
        CenterCursorOnce(view);
        if (view.Scene is null)
        {
            _driver.BeforeRepaint += painted =>
            {
                if (ReferenceEquals(painted, view))
                {
                    StepEffects(view, EffectTick());
                }
            };
        }

        if (view.Scene is { } sceneOutput)
        {
            sceneOutput.BeforeRepaint += tick => StepEffects(view, tick);
            AddPostStage(view);
            sceneOutput.ScanoutCandidateChanged += surface => OnScanoutCandidate(view, surface);
            sceneOutput.OffloadCandidatesChanged += candidates => OnOffloadCandidates(view, candidates);
            if (TraceEnabled)
            {
                sceneOutput.DamagePending += () => Trace("damage");
            }
        }

        if (TraceEnabled)
        {
            view.Output.Frame += () => Trace($"frame {view.Output.Name}");
        }

        _cursor.AddOutput(view.Output, view.Scene);

        if (view.Output is WaylandOutput hosted)
        {
            hosted.Committed += _ =>
            {
                if (!_probed && hosted.CurrentMode.Width > 0)
                {
                    _probed = true;
                    _report.Line($"PROBE decorated={hosted.Decorated} hostFrame={(hosted.HostFrame is null ? "none" : "yes")} insets={(hosted.HostFrame is null ? "-" : hosted.HostFrame.Insets.ToString())} mode={hosted.CurrentMode.Width}x{hosted.CurrentMode.Height}");
                }
            };
        }

        if (view.Output is Basin.Backend.Drm.DrmOutput drmOutput)
        {
            if (_outputsCreated)
            {
                _report.Line($"OUTPUT + {drmOutput.Name}");
            }

            _report.Line($"OUTPUT {drmOutput.Name} {drmOutput.Description} {drmOutput.PreferredMode.Width}x{drmOutput.PreferredMode.Height} scanout-modifiers={view.SwapModifiers.Length}{(view.IsSecondary ? " secondary" : "")}");

            view.ColorDescription = DescriptionOf(view.Output);
            view.KmsColorRouted = _colorConfiguration is { } routing && routing.RouteKmsPipeline(view.Output);
            _colorPack.AddOutput(view.Global, view.Output, view.Scene);
            SyncBlendSpace(view);
            RefreshSurfaceLuts();
            if (_panels.Match(drmOutput) is { } panel)
            {
                _report.Line($"PANEL {drmOutput.Name} {panel.Vendor}{(panel.Product is { } product ? $" 0x{product:x4}" : string.Empty)}");
            }

            if (_hdr && (_panels.Match(drmOutput)?.Hdr ?? drmOutput.Edid.SupportsPq))
            {
                var peak = _colorConfiguration?.ColorimetryOf(drmOutput)?.MaxLuminance ?? drmOutput.Edid.MaxLuminance;
                _report.Line($"HDR {drmOutput.Name} PQ peak={peak:F0}cd/m2 bt2020={drmOutput.Edid.SupportsBt2020}");
                if (view.Scene is { } hdrScene)
                {
                    hdrScene.OutputHdrMetadata = Basin.Color.OutputDescriptions.HdrMetadataFor(
                        DescriptionOf(drmOutput), _colorConfiguration?.ColorimetryOf(drmOutput)?.Chromaticities ?? drmOutput.Edid.Chromaticities);
                    hdrScene.HdrMetadataPassthrough = true;
                }
            }

            RefreshGammaBaseline(view);
            if (_nightLightKelvin is not null && drmOutput.GammaLutSize > 0)
            {
                _gamma.ApplyBaseline(view.Output);
            }
        }

        _colorPack.AddOutput(view.Global, view.Output, view.Scene);
        if (view.Output.Scale != 1)
        {
            _report.Line($"SCALE {view.Output.Name} {view.Output.Scale}");
        }
    }

    private void OnViewRemoved(OutputView view)
    {
        if (view.Output is Basin.Backend.Drm.DrmOutput)
        {
            _report.Line($"OUTPUT - {view.Output.Name}");
        }

        if (view == _swipeView)
        {
            AbortWorkspaceSwipe();
        }

        if (view == _overviewSwipeView)
        {
            _ = EndOverviewSwipe(cancelled: true, timeMs: 0);
        }

        UnshelveFromOutput(view, Views.FirstOrDefault(v => v != view && v.Tag is OutputPolicy));

        _presenceTracker.RemoveOutput(view.Output);
        _colorPack.RemoveOutput(view.Global);
        _cursor.RemoveOutput(view.Output);
        DropWorkspacesOf(view);
        view.Canvas.DisposeGlow(_glowShaders);
        OverviewOf(view).StepGlow.Dispose(_glowShaders);
        LeaveStep(view);
    }

    private void OnStampModeset(Basin.Backend.Drm.DrmOutput output, OutputState state)
    {
        if (_config.OutputSettingFor(output.Name) is { } setting)
        {
            if (setting.Mode is { } wanted &&
                FindMode(output, wanted.Width, wanted.Height, wanted.Refresh) is { } mode)
            {
                state.SetMode(mode);
            }

            if (setting.Transform is { } transform)
            {
                state.SetTransform(transform);
            }
        }

        var panel = _panels.Match(output);
        _colorConfiguration?.SetPanel(output, panel);
        output.ModeGenerator = panel?.ModeGenerator ?? Basin.Capabilities.OutputModeGenerator.Auto;
        output.MaxClockKhz = panel?.MaxClockKhz ?? 0;
        if (panel is not null)
        {
            if (_config.OutputSettingFor(output.Name)?.Transform is null && panel.Orientation is { } orientation)
            {
                state.SetTransform(orientation);
            }

            if (panel.RefreshMilliHz.Count > 0 && output.PreferredMode.Width > 0)
            {
                var preferred = output.PreferredMode;
                state.SetCustomModes([.. panel.RefreshMilliHz.Select(rate => new OutputMode(preferred.Width, preferred.Height, rate))]);
            }
        }

        var driveHdr = _hdr && (panel?.Hdr ?? output.Edid.SupportsPq);
        var hdrNow = driveHdr && (!_config.HdrDemand || _hdrShown);
        if (driveHdr && _colorConfiguration is { } colorConfiguration &&
            (colorConfiguration.Supported(output) &
             Basin.Capabilities.OutputConfigurationFeatures.HighDynamicRange) != 0)
        {
            colorConfiguration.Seed(
                output,
                new Basin.Color.OutputColorState { HighDynamicRange = hdrNow, HdrOnDemand = _config.HdrDemand, Source = _colorSource });
        }
        else if (_colorSource != Basin.Capabilities.OutputColorProfileSource.Edid &&
            _colorConfiguration is { } sdrConfiguration)
        {
            sdrConfiguration.Seed(
                output, new Basin.Color.OutputColorState { Source = _colorSource, IccProfilePath = _iccProfile });
        }

        if (hdrNow)
        {
            state.SetHdr(Basin.Color.OutputDescriptions.HdrMetadataFor(
                DescriptionOf(output), _colorConfiguration?.ColorimetryOf(output)?.Chromaticities ?? output.Edid.Chromaticities));
        }

        ApplyColorLooks(output, driveHdr);
    }

    private static OutputMode? FindMode(Basin.Backend.Drm.DrmOutput output, int width, int height, int? refresh)
    {
        foreach (var mode in output.Modes)
        {
            if (mode.Width == width && mode.Height == height &&
                (refresh is not { } wanted || (int)Math.Round(mode.RefreshMilliHz / 1000.0) == wanted))
            {
                return mode;
            }
        }

        return null;
    }

    private void StepEffects(OutputView view, FrameTick tick)
    {
        UpdatePlacements(view);
        var running = _effects.Step(tick);
        if (_feedback is { } feedback)
        {
            running |= feedback.Step(tick);
        }

        running |= _post.Step(tick, view.Width, view.Height);
        running |= StepCanvasMotions(tick);
        running |= StepShelfMotions(tick);
        running |= StepOverviews(tick);
        running |= StepGridReveals(tick);
        running |= StepTilts(tick);
        running |= StepPhysics(tick);
        running |= StepGridPinches(tick);
        running |= StepGridLights(tick);
        if (running)
        {
            for (var i = 0; i < Views.Count; i++)
            {
                Views[i].Scheduler?.ScheduleRepaint();
            }
        }
    }

    private void OnPainted(OutputView view)
    {
        UpdateSurfacePresence();
        MaybeScreenshotDamage(view);
    }

    private void OnLayoutChanged()
    {
        ArrangeLayerSurfaces();
        _lockDriver.Reconfigure();
        LayoutCanvasAll();
    }

    private void OnModeChanged(OutputView view)
    {
        ReapplyPinnedGeometry();
        LayoutCanvasAll();
    }

    private void OnTransformChanged(OutputView view, OutputTransform was)
    {
        _ = _post.BeginRotation(view.LastPresentedBuffer, was, view.Output.Transform, EffectTick());
        view.Scheduler?.ScheduleRepaint();
    }

    private void OnScanoutChanged(OutputView view, ScanoutChoice choice)
    {
        if (choice == ScanoutChoice.DeviceBuffers && view.Output is WaylandOutput)
        {
            _log.Info($"{_rendererName} zero-copy: {view.SwapModifiers.Length} modifiers for {DrmFormat.Xrgb8888}");
        }
        else if (choice == ScanoutChoice.DumbLinear && _renderer.Device is not null)
        {
            _log.Warn(
                $"{view.Output.Name}: the renderer shares no scanout format with this plane; " +
                $"presenting through CPU-mapped buffers, which reads the whole framebuffer back every frame");
        }
        else if (choice == ScanoutChoice.RefusedByPlane)
        {
            _log.Warn($"{view.Output.Name}: device buffers refused by the plane; falling back to dumb linear");
        }
    }

    private void DumpTree(SceneNode node, int depth)
    {
        var info = node switch
        {
            SceneBuffer b => $"buffer {(b.Buffer is null ? "empty" : $"{b.Buffer.Width}x{b.Buffer.Height}")} opaque={b.IsOpaque}",
            SceneRect r => $"rect {r.Width}x{r.Height}",
            SceneTransform { Deformer: CanvasWarpTransform canvas } => $"transform canvas sceneX={canvas.SceneX}",
            SceneTransform { Deformer: { } deformer } => $"transform {deformer.GetType().Name}",
            SceneTransform => "transform",
            SceneTree => "tree",
            _ => node.GetType().Name,
        };
        _log.Debug($"DBG {(new string(' ', depth * 2))}{info} at ({node.X},{node.Y}) enabled={node.Enabled}");
        if (node is SceneTree tree)
        {
            foreach (var child in tree.Children)
            {
                DumpTree(child, depth + 1);
            }
        }
    }

    private readonly List<SurfaceBox> _presence = [];
    private SurfacePresenceTracker _presenceTracker = null!;

    private void UpdateSurfacePresence()
    {
        _scene.CollectSurfaces(_presence);
        _presenceTracker.Update(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_presence));
    }

    private readonly HashSet<Surface> _offloadFeedback = [];
    private readonly List<Surface> _offloadFeedbackGone = [];

    private void OnOffloadCandidates(OutputView view, IReadOnlyList<Basin.Scene.SceneBuffer> candidates)
    {
        if (_dmabufGlobal is null ||
            view.Output is not Basin.Backend.Drm.DrmOutput drmOutput ||
            drmOutput.OverlayScanoutFormats.Count == 0)
        {
            return;
        }

        _offloadFeedbackGone.Clear();
        foreach (var surface in _offloadFeedback)
        {
            var still = false;
            for (var i = 0; i < candidates.Count && !still; i++)
            {
                still = candidates[i].InputSurface == surface;
            }

            if (!still)
            {
                _offloadFeedbackGone.Add(surface);
            }
        }

        foreach (var surface in _offloadFeedbackGone)
        {
            _offloadFeedback.Remove(surface);
            if (surface != _scanoutFeedbackSurface)
            {
                _dmabufGlobal.SetScanoutTargets(surface, null);
            }
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].InputSurface is { } surface &&
                surface != _scanoutFeedbackSurface &&
                _offloadFeedback.Add(surface))
            {
                _dmabufGlobal.SetScanoutTargets(surface, drmOutput.OverlayScanoutFormats);
            }
        }
    }

    private void OnScanoutCandidate(OutputView view, Surface? surface)
    {
        if (_dmabufGlobal is null)
        {
            return;
        }

        if (_scanoutFeedbackSurface is { } previous && previous != surface)
        {
            _dmabufGlobal.SetScanoutTargets(previous, null);
        }

        _scanoutFeedbackSurface = surface;
        if (surface is not null && view.Output is Basin.Backend.Drm.DrmOutput drmOutput)
        {
            _dmabufGlobal.SetScanoutTargets(surface, drmOutput.ScanoutFormats);
        }
        else if (surface is not null && view.Output is WaylandOutput nested && nested.ScanoutFormats.Count > 0)
        {
            _dmabufGlobal.SetScanoutTargets(surface, nested.ScanoutFormats);
        }
    }

    private void MaybeScreenshotDamage(OutputView view)
    {
        if (_shotPath is null || view != Views[_shotView])
        {
            return;
        }

        var box = view.Scene?.ReplicationSource is null
            ? view.Box
            : new Box(0, 0, view.Width, view.Height);
        _scene.Root.SetPosition(-box.X, -box.Y);
        MaybeScreenshot(view);
        _scene.Root.SetPosition(0, 0);
    }

    private RenderColor Background => _config.Background;

    private void ReapplyPinnedGeometry()
    {
        foreach (var window in _windows)
        {
            window.ReapplyPinnedGeometry();
        }

        foreach (var xwindow in _xwindows)
        {
            xwindow.ReapplyPinnedGeometry();
        }
    }

    private static readonly double[] ScaleSteps = [1, 1.25, 1.5, 2];

    private void CycleScale()
    {
        var view = Views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output)
            ?? Views.FirstOrDefault();
        if (view is null)
        {
            return;
        }

        var index = Array.FindIndex(ScaleSteps, s => Math.Abs(s - view.Output.Scale) < 0.001);
        SetOutputScale(view, ScaleSteps[(index + 1) % ScaleSteps.Length]);
    }

    private void SetOutputScale(OutputView view, double scale)
    {
        _driver.SetScale(view, scale);
        if (view.Output.Scale != scale)
        {
            return;
        }

        _report.Line($"SCALE {view.Output.Name} {view.Output.Scale}");
        RefreshOutputLayout();
    }

    private void RefreshOutputColor(OutputView view)
    {
        if (_colorConfiguration is not { } colorConfiguration)
        {
            return;
        }

        var description = colorConfiguration.DescriptionOf(view.Output);
        if (Basin.Capabilities.ImageDescription.ContentComparer.Equals(description, view.ColorDescription))
        {
            return;
        }

        view.ColorDescription = description;
        view.KmsColorRouted = colorConfiguration.RouteKmsPipeline(view.Output);
        RefreshGammaBaseline(view);
        _colorPack.Outputs?.Refresh();
        SyncBlendSpace(view);
        RefreshSurfaceLuts();
        view.Scheduler?.ScheduleRepaint();
    }

    private static bool TouchesColor(in OutputConfigurationEntry entry) =>
        entry.HighDynamicRange is not null || entry.WideColorGamut is not null ||
        entry.SdrBrightnessNits is not null || entry.SdrGamutWideness is not null ||
        entry.ColorProfileSource is not null || entry.IccProfilePath is not null ||
        entry.HdrColorProfileSource is not null || entry.HdrIccProfilePath is not null ||
        entry.BrightnessOverrides is not null;

    private void OnOutputConfigurationApplied(IReadOnlyList<OutputConfigurationEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (TouchesColor(entry) && Views.FirstOrDefault(v => v.Output == entry.Output) is { } colorView)
            {
                RefreshOutputColor(colorView);
            }
        }

        foreach (var entry in entries)
        {
            if (entry.ReplicationSourceUuid is not { } uuid ||
                Views.FirstOrDefault(v => v.Output == entry.Output) is not { } replicaView)
            {
                continue;
            }

            replicaView.ReplicaSource = uuid.Length > 0
                ? Views.FirstOrDefault(v =>
                    v != replicaView && Basin.Desktop.OutputUuid.For(v.Output) == uuid)
                : null;
            replicaView.Scheduler?.ScheduleRepaint();
        }

        foreach (var entry in entries)
        {
            if (Views.FirstOrDefault(v => v.Output == entry.Output) is { } view &&
                entry is { Enabled: true, Position: not null })
            {
                view.AutoLayout = false;
            }
        }

        _driver.Relayout();
        RefreshOutputLayout();
        foreach (var entry in entries)
        {
            var box = _layout.BoxOf(entry.Output);
            _report.Line($"CONFIGURED {entry.Output.Name} enabled={(entry.Enabled ? "yes" : "no")} " + $"{box.Width}x{box.Height}+{box.X}+{box.Y} scale {entry.Output.Scale}");
        }
    }

    private void RefreshOutputLayout()
    {
        ArrangeLayerSurfaces();
        LayoutCanvasAll();
        ReapplyPinnedGeometry();
        UpdateSurfacePresence();
        foreach (var window in _windows)
        {
            window.RefreshFrame();
        }

        foreach (var xwindow in _xwindows)
        {
            xwindow.Layout();
        }

        if (_pointer is { } pointer)
        {
            pointer.Reposition();
        }

        foreach (var view in Views)
        {
            if (!_layout.Contains(view.Output))
            {
                continue;
            }

            if (_fullRepaint)
            {
                _driver.RepaintNow(view);
            }
            else
            {
                view.Scheduler?.ScheduleRepaint();
            }
        }
    }

    private IDisposable? AttachHostChrome(WaylandOutput hosted, WaylandHostFrame frame)
    {
        var lease = new HostChromeLease(hosted, frame, CreateHostChrome);
        _hostChrome.Add(lease);
        return lease;
    }

    private IDisposable CreateHostChrome(WaylandOutput hosted, WaylandHostFrame frame)
    {
        if (CreateFrameRenderer() is not { } renderer)
        {
            return Basin.Backend.Wayland.Skia.DefaultHostChrome.Attach(hosted, frame);
        }

        if (FrameHosts.For(renderer) is not { } host)
        {
            _log.Error($"host frame: {FrameHosts.Describe(renderer)}");
            return Basin.Backend.Wayland.Skia.DefaultHostChrome.Attach(hosted, frame);
        }

        var chrome = new FrameHostChrome(frame, host, renderer, $"basin — {hosted.Name}", onClose: () => _runLoop.Stop());
        hosted.Committed += chrome.OnOutputChanged;
        return new FrameHostChromeBinding(hosted, chrome);
    }

    private void RebuildHostChrome()
    {
        foreach (var lease in _hostChrome)
        {
            lease.Rebuild();
        }
    }

    private void UpdatePlacements(OutputView view)
    {
        var output = view.Box;
        foreach (var window in _windows)
        {
            Place(window.Frame, window.X, window.Y, window.GeometrySize, output);
        }

        foreach (var xwindow in _xwindows)
        {
            Place(xwindow.Frame, xwindow.X, xwindow.Y, xwindow.GeometrySize, output);
        }
    }

    private static void Place(Basin.Scene.Frame? frame, int x, int y, (int Width, int Height) size, Box output)
    {
        if (frame is null || output.Width <= 0 || output.Height <= 0)
        {
            return;
        }

        var centerX = x + (size.Width / 2);
        var centerY = y + (size.Height / 2);
        if (centerX < output.X || centerX >= output.X + output.Width || centerY < output.Y || centerY >= output.Y + output.Height)
        {
            return;
        }

        frame.Placement = new Basin.Capabilities.FramePlacement(x - output.X, y - output.Y, output.Width, output.Height);
    }
}
