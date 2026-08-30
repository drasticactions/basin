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
            : Basin.Capabilities.ImageDescription.Srgb;

    private void DeclareColor()
    {
        if (_color is { } color)
        {
            Basin.Desktop.SurfaceLutDriver.Declare(color, Views.Select(v => DescriptionOf(v.Output)));
        }
    }

    private void ReportFallback(Basin.Renderers.RendererFallback fallback) =>
        _log.Warn($"{(fallback.Describe())}");

    private void WireOutputDriver()
    {
        _driver.Arrange = ArrangeOutputs;
        _driver.ConfiguredScale = output => _config.OutputSettingFor(output.Name)?.Scale;
        _driver.Added += OnViewAdded;
        _driver.Removed += OnViewRemoved;
        _driver.Emptied += () => _runLoop.Stop();
        _driver.Painted += OnPainted;
        _driver.ModeChanged += OnModeChanged;
        _driver.TransformChanged += OnTransformChanged;
        _driver.StampFrame += OnStampFrame;
        _driver.StampModeset += OnStampModeset;
        _driver.ScanoutChanged += OnScanoutChanged;
        _driver.ScaleRefused += (view, scale) => _log.Warn($"scale {scale} refused by {view.Output.Name}");
        _driver.ModesetRefused += card => _log.Error($"modeset refused by {card.Name}");
        _driver.HostScaleFollowed += view =>
        {
            BasinReport.Line($"SCALE {view.Output.Name} {view.Output.Scale}");
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
        if (_config.OutputSettingFor(view.Output.Name)?.Aspect is { } aspect && aspect > 0 &&
            view.Output.AspectRatio != aspect)
        {
            SetOutputAspect(view, aspect);
        }

        InitWorkspaces(view);
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
                    BasinReport.Line($"PROBE decorated={hosted.Decorated} hostFrame={(hosted.HostFrame is null ? "none" : "yes")} insets={(hosted.HostFrame is null ? "-" : hosted.HostFrame.Insets.ToString())} mode={hosted.CurrentMode.Width}x{hosted.CurrentMode.Height}");
                }
            };
            hosted.HostFrameAvailable += frame =>
            {
                if (CreateFrameRenderer() is not { } renderer)
                {
                    return;
                }

                var chrome = new HostChrome(this, frame, renderer, $"basin — {hosted.Name}", () => _runLoop.Stop());
                _hostChrome.Add(chrome);
                hosted.Committed += chrome.OnOutputChanged;
            };
        }

        if (view.Output is Basin.Backend.Drm.DrmOutput drmOutput)
        {
            if (_outputsCreated)
            {
                BasinReport.Line($"OUTPUT + {drmOutput.Name}");
            }

            BasinReport.Line($"OUTPUT {drmOutput.Name} {drmOutput.Description} {drmOutput.PreferredMode.Width}x{drmOutput.PreferredMode.Height} scanout-modifiers={view.SwapModifiers.Length}{(view.IsSecondary ? " secondary" : "")}");

            view.ColorDescription = DescriptionOf(view.Output);
            view.KmsColorRouted = _colorConfiguration is { } routing && routing.RouteKmsPipeline(view.Output);
            DeclareColor();
            _color.SetOutputDescription(view.Global, view.ColorDescription);
            RefreshSurfaceLuts();
            if (_hdr && drmOutput.Edid.SupportsPq)
            {
                BasinReport.Line($"HDR {drmOutput.Name} PQ peak={drmOutput.Edid.MaxLuminance:F0}cd/m2 bt2020={drmOutput.Edid.SupportsBt2020}");
            }

            RefreshGammaBaseline(view);
            if (_nightLightKelvin is not null && drmOutput.GammaLutSize > 0)
            {
                _gamma.ApplyBaseline(view.Output);
            }
        }

        if (view.Output.Scale != 1)
        {
            BasinReport.Line($"SCALE {view.Output.Name} {view.Output.Scale}");
        }
    }

    private void OnViewRemoved(OutputView view)
    {
        if (view.Output is Basin.Backend.Drm.DrmOutput)
        {
            BasinReport.Line($"OUTPUT - {view.Output.Name}");
        }

        if (view == _swipeView)
        {
            AbortWorkspaceSwipe();
        }

        _presenceTracker.RemoveOutput(view.Output);
        _cursor.RemoveOutput(view.Output);
        DropWorkspacesOf(view);
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

            if (setting.Aspect is { } aspect)
            {
                state.SetAspectRatio(aspect);
            }
        }

        var driveHdr = _hdr && output.Edid.SupportsPq;
        if (driveHdr && _colorConfiguration is { } colorConfiguration &&
            (colorConfiguration.Supported(output) &
             Basin.Capabilities.OutputConfigurationFeatures.HighDynamicRange) != 0)
        {
            colorConfiguration.Seed(
                output,
                new Basin.Color.OutputColorState { HighDynamicRange = true, Source = _colorSource });
        }
        else if (_colorSource != Basin.Capabilities.OutputColorProfileSource.Edid &&
            _colorConfiguration is { } sdrConfiguration)
        {
            sdrConfiguration.Seed(
                output, new Basin.Color.OutputColorState { Source = _colorSource, IccProfilePath = _iccProfile });
        }

        if (driveHdr)
        {
            state.SetHdr(Basin.Color.OutputDescriptions.HdrMetadataFor(
                DescriptionOf(output), output.Edid.Chromaticities));
        }
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

    private void OnStampFrame(OutputView view, OutputState state)
    {
        var autoVrr = false;
        if (_scanoutFeedbackSurface is { } scanoutSurface)
        {
            if (_tearing.PrefersTearing(scanoutSurface))
            {
                state.SetTearing(true);
            }

            var contentType = _contentType.TypeOf(scanoutSurface);
            if (contentType is Basin.Desktop.ContentTypeManager.ContentType.Game
                    or Basin.Desktop.ContentTypeManager.ContentType.Video &&
                VrrPolicyOf(view.Output) == Basin.Capabilities.OutputVrrPolicy.Automatic)
            {
                state.SetAdaptiveSync(true);
                autoVrr = true;
            }
        }

        if (!autoVrr && view.AutoVrrActive)
        {
            state.SetAdaptiveSync(false);
        }

        view.AutoVrrActive = autoVrr;
    }

    private void StepEffects(OutputView view, FrameTick tick)
    {
        var running = _effects.Step(tick);
        if (_feedback is { } feedback)
        {
            running |= feedback.Step(tick);
        }

        running |= _post.Step(tick, view.Width, view.Height);
        if (running)
        {
            foreach (var animated in Views)
            {
                animated.Scheduler?.ScheduleRepaint();
            }
        }
    }

    private void OnPainted(OutputView view)
    {
        UpdateSurfacePresence();
        MaybeScreenshotDamage(view);
    }

    private void OnModeChanged(OutputView view) => ReapplyPinnedGeometry();

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

    private void AdoptSecondaryCard(DrmDeviceInfo device)
    {
        try
        {
            var backend = new Basin.Backend.Drm.DrmBackend(_loop, _session!, device.CardPath);
            backend.Start();
            _secondaryBackends.Add(backend);
            var allocator = new Basin.Backend.Drm.DumbAllocator(backend);
            _secondaryAllocators.Add(allocator);
            BasinReport.Line($"CARD + {device.CardPath} ({device.Driver})");
            foreach (var output in backend.Outputs)
            {
                AddSecondaryOutput(output, allocator);
            }

            backend.OutputAdded += output =>
            {
                BasinReport.Line($"OUTPUT + {output.Name}");
                AddSecondaryOutput(output, allocator);
            };
            backend.OutputRemoved += output =>
            {
                if (_driver.ViewOf(output) is { } view)
                {
                    _driver.RemoveView(view);
                }
            };
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            BasinReport.Line($"CARD {device.CardPath} not adopted: {e.Message}");
        }
    }

    private void AddSecondaryOutput(Basin.Backend.Drm.DrmOutput output, IAllocator allocator)
    {
        if (!_driver.EnableWithStamp(output))
        {
            _log.Error($"modeset refused by {output.Name}");
            return;
        }

        _ = _driver.AddView(output, allocator, secondary: true);
    }

    private static readonly RenderColor Background = new(0.09f, 0.1f, 0.12f, 1f);

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

        BasinReport.Line($"SCALE {view.Output.Name} {view.Output.Scale}");
        RefreshOutputLayout();
    }

    private void SetOutputAspect(OutputView view, double aspect)
    {
        _driver.SetAspectRatio(view, aspect);
        if (view.Output.AspectRatio != aspect)
        {
            return;
        }

        var (width, height) = view.Output.LogicalSize();
        BasinReport.Line($"ASPECT {view.Output.Name} {aspect} logical={width}x{height}");
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
        _color.SetOutputDescription(view.Global, description);
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
            BasinReport.Line($"CONFIGURED {entry.Output.Name} enabled={(entry.Enabled ? "yes" : "no")} " + $"{box.Width}x{box.Height}+{box.X}+{box.Y} scale {entry.Output.Scale}");
        }
    }

    private void RefreshOutputLayout()
    {
        ArrangeLayerSurfaces();
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

    private Basin.Capabilities.OutputVrrPolicy VrrPolicyOf(IOutput output) =>
        _outputConfiguration is { } configuration && configuration.TryRead(output, out var state) &&
        state.VrrPolicy is { } policy
            ? policy
            : Basin.Capabilities.OutputVrrPolicy.Automatic;
}
