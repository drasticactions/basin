using System.Diagnostics;
using Basin;
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
            Basin.Desktop.SurfaceLutDriver.Declare(color, _views.Select(v => DescriptionOf(v.Output)));
        }
    }

    private void ReportFallback(Basin.Renderers.RendererFallback fallback) =>
        _log.Warn($"{(fallback.Describe())}");

    private double ScaleFor(int index) =>
        _scales.Length == 0 ? 1 : _scales[Math.Min(index, _scales.Length - 1)];

    private double FollowedScaleFor(int index, OutputBase output) =>
        _scales.Length == 0 && output is WaylandOutput hosted ? hosted.HostScale : ScaleFor(index);

    private void AddOutput()
    {
        var output = _backend!.CreateOutput();
        var view = new OutputView(output, new OutputGlobal(_display, output));
        _views.Add(view);
        _presenceTracker.AddOutput(view.Output, view.Global);
        InitWorkspaces(view);
        var scale = ScaleFor(_views.Count - 1);
        if (scale != 1)
        {
            using var state = new OutputState();
            output.Commit(state.SetScale(scale));
        }

        output.HostScaleChanged += () =>
        {
            if (_scales.Length == 0 && output.HostScale != output.Scale)
            {
                SetOutputScale(view, output.HostScale);
            }
        };

        _layout.Add(output, 0, 0);
        Relayout();
        output.CloseRequested += () => _runLoop.Stop();
        output.Committed += _ => OnOutputChanged(view);
        output.Committed += _ => { if (!_probed && output.CurrentMode.Width > 0) { _probed = true; BasinReport.Line($"PROBE decorated={output.Decorated} hostFrame={(output.HostFrame is null ? "none" : "yes")} insets={(output.HostFrame is null ? "-" : output.HostFrame.Insets.ToString())} mode={output.CurrentMode.Width}x{output.CurrentMode.Height}"); } };
        WireRepaint(view);
        _cursor.AddOutput(view.Output, view.SceneOutput);

        output.HostFrameAvailable += frame =>
        {
            if (CreateFrameRenderer() is not { } renderer)
            {
                return;
            }

            var chrome = new HostChrome(this, frame, renderer, $"basin — {output.Name}", () => _runLoop.Stop());
            _hostChrome.Add(chrome);
            output.Committed += chrome.OnOutputChanged;
        };
    }

    private void WireRepaint(OutputView view)
    {
        if (_fullRepaint)
        {
            view.Output.Frame += () => RenderOutput(view);
            return;
        }

        view.Scheduler = new OutputScheduler(_loop, view.Output);
        view.Scheduler.Repaint += () => Repaint(view);
        if (view.IsSecondary)
        {
            _scene.Damaged += (_, box) =>
            {
                var outputBox = view.ReplicaSource is { } replicaSource
                    ? _layout.BoxOf(replicaSource.Output)
                    : _layout.BoxOf(view.Output);
                if (!outputBox.IsEmpty && box.X < outputBox.X + outputBox.Width && box.X + box.Width > outputBox.X &&
                    box.Y < outputBox.Y + outputBox.Height && box.Y + box.Height > outputBox.Y)
                {
                    view.Scheduler.ScheduleRepaint();
                }
            };
            return;
        }

        view.SceneOutput = new SceneOutput(_scene, view.Output);
        view.SceneOutput.BeforeRepaint += tick =>
        {
            var running = _effects.Step(tick);
            if (_feedback is { } feedback)
            {
                running |= feedback.Step(tick);
            }

            running |= _post.Step(tick, view.Width, view.Height);
            if (running)
            {
                foreach (var animated in _views)
                {
                    animated.Scheduler?.ScheduleRepaint();
                }
            }
        };

        AddPostStage(view);

        _dmabufCapture.Track(view.Output, view.SceneOutput);

        view.SceneOutput.DamagePending += view.Scheduler.ScheduleRepaint;
        view.SceneOutput.ScanoutCandidateChanged += surface => OnScanoutCandidate(view, surface);
        view.SceneOutput.OffloadCandidatesChanged += candidates => OnOffloadCandidates(view, candidates);
        if (view.Output is IPresentingOutput presenting)
        {
            presenting.PresentedOnScreen += (timeNs, refreshNs, sequence) =>
            {
                view.PresentDiscarded = false;
                view.LastPresent = (timeNs, refreshNs, sequence);
                view.Scheduler!.NotifyPresented((long)timeNs);
            };
            presenting.PresentationDiscarded += () =>
            {
                view.LastPresent = null;
                view.PresentDiscarded = true;
            };
        }

        view.Output.Frame += () =>
        {
            if (!view.FrameDonesPending)
            {
                return;
            }

            view.FrameDonesPending = false;
            if (view.PresentDiscarded)
            {
                view.PresentDiscarded = false;
                _presentation.DiscardAll();
                _frameClock.EndFrame(view.Output, 0);
            }
            else if (view.LastPresent is { } present)
            {
                view.LastPresent = null;
                _presentation.PresentAll(view.Output, present.TimeNs, present.RefreshNs, present.Sequence,
                    PresentedFlags.Vsync | PresentedFlags.HwClock | PresentedFlags.HwCompletion);
                _frameClock.EndFrame(view.Output, (long)present.TimeNs);
            }
            else
            {
                _presentation.PresentAllNow(view.Output);
                _frameClock.EndFrame(view.Output, MonotonicClock.Nanos);
            }
        };
        if (TraceEnabled)
        {
            view.SceneOutput.DamagePending += () => Trace("damage");
            view.Output.Frame += () => Trace($"frame {view.Output.Name}");
        }
    }

    private PresentationTimeGlobal _presentation = null!;

    private void Repaint(OutputView view)
    {
        _frameClock.BeginFrame(view.Output, view.Scheduler!.PredictedVblankNanos);
        if (view.IsSecondary)
        {
            SecondaryRepaint(view);
            return;
        }

        if (view.Swapchain is null || view.SceneOutput is null)
        {
            return;
        }

        if (_frames > 0)
        {
            view.SceneOutput.Ring.AddWhole();
        }

        var box = new Box(0, 0, view.Width, view.Height);
        if (view.ReplicaSource is { } replicaSource && _layout.Contains(replicaSource.Output))
        {
            view.SceneOutput.ReplicationSource = _layout.BoxOf(replicaSource.Output);
        }
        else
        {
            view.SceneOutput.ReplicationSource = null;
            if (_layout.Contains(view.Output))
            {
                box = _layout.BoxOf(view.Output);
                view.SceneOutput.Position = new Point(box.X, box.Y);
            }
        }

        var renderStart = TraceEnabled ? Stopwatch.GetTimestamp() : 0;
        var options = new SceneCommitOptions
        {
            Background = Background,
            DebugDamageTint = _damageTint,
            AllowPlaneOffload = _offload,
            TargetPresentNanos = Math.Max(
                view.Scheduler!.PredictedVblankNanos,
                (long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency))),
        };
        _frameState.Clear();
        var autoVrr = false;
        if (_scanoutFeedbackSurface is { } scanoutSurface)
        {
            if (_tearing.PrefersTearing(scanoutSurface))
            {
                _frameState.SetTearing(true);
            }

            var contentType = _contentType.TypeOf(scanoutSurface);
            if (contentType is Basin.Desktop.ContentTypeManager.ContentType.Game
                    or Basin.Desktop.ContentTypeManager.ContentType.Video &&
                VrrPolicyOf(view.Output) == Basin.Capabilities.OutputVrrPolicy.Automatic)
            {
                _frameState.SetAdaptiveSync(true);
                autoVrr = true;
            }
        }

        if (!autoVrr && view.AutoVrrActive)
        {
            _frameState.SetAdaptiveSync(false);
        }

        view.AutoVrrActive = autoVrr;

        var committed = view.SceneOutput.Commit(_renderer, view.Swapchain, _frameState, options);
        var refused = !committed && view.SceneOutput.NeedsRepaint;
        if (committed)
        {
            view.Scheduler!.NotifyCommitted();
            view.LastPresentedBuffer = _frameState.Buffer;
            view.Rendered++;
        }
        else if (refused)
        {
            view.Scheduler!.ScheduleRepaint();
        }

        if (TraceEnabled)
        {
            var renderMs = (Stopwatch.GetTimestamp() - renderStart) * 1000.0 / Stopwatch.Frequency;
            Trace($"repaint committed={committed} refused={refused} renderMs={renderMs:F2}");
        }

        MaybeScreenshotDamage(view, box);
        if (refused)
        {
            return;
        }

        _capture.NotifyDamaged(view.Output, new Box(0, 0, view.Width, view.Height));
        UpdateSurfacePresence();
        if (committed)
        {
            view.FrameDonesPending = true;
        }

        _scene.SendFrameDone((uint)Environment.TickCount);
        if (_frames > 0)
        {
            view.Scheduler!.ScheduleRepaint();
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

    private bool RenderForCapture(IOutput output, IBuffer target)
    {
        var box = _layout.BoxOf(output);
        _scene.Root.SetPosition(-box.X, -box.Y);
        var ok = _scene.Render(_renderer, target, SceneOptions(output));
        _scene.Root.SetPosition(0, 0);
        return ok;
    }

    private Box ToplevelBox(Basin.Shell.Xdg.XdgToplevelWindow toplevel)
    {
        var window = _windows.FirstOrDefault(w => w.Toplevel == toplevel);
        var surface = toplevel.Surface.Current;
        return window is null
            ? default
            : new Box(window.X, window.Y, surface.Width, surface.Height);
    }

    private double ScaleOfBox(Box box)
    {
        var scale = 1.0;
        foreach (var view in _views)
        {
            var outputBox = _layout.BoxOf(view.Output);
            if (box.X < outputBox.Right && box.Right > outputBox.X &&
                box.Y < outputBox.Bottom && box.Bottom > outputBox.Y)
            {
                scale = Math.Max(scale, view.Output.Scale);
            }
        }

        return scale;
    }

    private bool RenderToplevelCapture(Basin.Shell.Xdg.XdgToplevelWindow toplevel, IBuffer target)
    {
        var box = ToplevelBox(toplevel);
        if (box.Width <= 0 || box.Height <= 0)
        {
            return false;
        }

        _scene.Root.SetPosition(-box.X, -box.Y);
        var ok = _scene.Render(_renderer, target, SceneOptions(ScaleOfBox(box)));
        _scene.Root.SetPosition(0, 0);
        return ok;
    }

    private void MaybeScreenshotDamage(OutputView view, Box box)
    {
        if (_shotPath is null || view != _views[_shotView])
        {
            return;
        }

        _scene.Root.SetPosition(-box.X, -box.Y);
        MaybeScreenshot(view);
        _scene.Root.SetPosition(0, 0);
    }

    private void RemoveDrmOutput(Basin.Backend.Drm.DrmOutput output)
    {
        BasinReport.Line($"OUTPUT - {output.Name}");
        var view = _views.FirstOrDefault(v => v.Output == output);
        if (view is not null)
        {
            if (view == _swipeView)
            {
                AbortWorkspaceSwipe();
            }

            _views.Remove(view);
            _presenceTracker.RemoveOutput(view.Output);
            DropWorkspacesOf(view);
            _dmabufCapture.Forget(view.Output);
            view.Swapchain?.Dispose();
            view.Global.Dispose();
        }

        Relayout();
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
                AddDrmOutput(output, allocator, secondary: true);
            }

            backend.OutputAdded += output =>
            {
                BasinReport.Line($"OUTPUT + {output.Name}");
                AddDrmOutput(output, allocator, secondary: true);
                Relayout();
            };
            backend.OutputRemoved += RemoveDrmOutput;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            BasinReport.Line($"CARD {device.CardPath} not adopted: {e.Message}");
        }
    }

    private void AddDrmOutput(Basin.Backend.Drm.DrmOutput output, IAllocator? allocator = null, bool secondary = false)
    {
        var view = new OutputView(output, new OutputGlobal(_display, output))
        {
            IsSecondary = secondary,
            Allocator = allocator ?? _allocator,
        };
        _views.Add(view);
        _presenceTracker.AddOutput(view.Output, view.Global);
        InitWorkspaces(view);
        _layout.Add(output, 0, 0);
        Relayout();
        output.Committed += _ => OnOutputChanged(view);
        WireRepaint(view);
        _cursor.AddOutput(view.Output, view.SceneOutput);

        view.SwapModifiers = view.Allocator!.Formats.Intersect(output.ScanoutFormats).ModifiersOf(_swapFormat).ToArray();
        if (view.Allocator is not Basin.Backend.Drm.DumbAllocator &&
            !view.Allocator!.CanScanOut(output, view.SwapModifiers, DrmFormat.Xrgb8888))
        {
            _log.Warn(
                $"{output.Name}: the renderer shares no scanout format with this plane; " +
                $"presenting through CPU-mapped buffers, which reads the whole framebuffer back every frame");
            view.Allocator = new Basin.Backend.Drm.DumbAllocator(_drm!);
            view.SwapModifiers = [];
        }

        if (!secondary)
        {
            _swapModifiers = view.SwapModifiers;
        }

        BasinReport.Line($"OUTPUT {output.Name} {output.Description} {output.PreferredMode.Width}x{output.PreferredMode.Height} scanout-modifiers={view.SwapModifiers.Length}{(secondary ? " secondary" : "")}");

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

        view.ColorDescription = DescriptionOf(output);
        view.KmsColorRouted = _colorConfiguration is { } routing && routing.RouteKmsPipeline(output);
        DeclareColor();
        _color.SetOutputDescription(view.Global, view.ColorDescription);
        RefreshSurfaceLuts();
        if (driveHdr)
        {
            BasinReport.Line($"HDR {output.Name} PQ peak={output.Edid.MaxLuminance:F0}cd/m2 bt2020={output.Edid.SupportsBt2020}");
        }

        _frameState.Clear();
        _frameState.SetEnabled(true).SetMode(output.PreferredMode).SetScale(ScaleFor(_views.Count - 1));
        if (driveHdr)
        {
            _frameState.SetHdr(Basin.Color.OutputDescriptions.HdrMetadataFor(view.ColorDescription, output.Edid.Chromaticities));
        }

        output.Commit(_frameState);
        RefreshGammaBaseline(view);
        if (_nightLightKelvin is not null && output.GammaLutSize > 0)
        {
            _gamma.ApplyBaseline(view.Output);
        }

        if (_fullRepaint)
        {
            RenderOutput(view);
        }
        else
        {
            view.Scheduler!.ScheduleRepaint();
        }
    }

    private static readonly RenderColor Background = new(0.09f, 0.1f, 0.12f, 1f);

    private void OnOutputChanged(OutputView view)
    {
        if (_transforms.TryGetValue(view, out var was) && was != view.Output.Transform)
        {
            _ = _post.BeginRotation(view.LastPresentedBuffer, was, view.Output.Transform, EffectTick());
            view.Scheduler?.ScheduleRepaint();
        }

        _transforms[view] = view.Output.Transform;

        var mode = view.Output.CurrentMode;
        var resized = view.Width != mode.Width || view.Height != mode.Height;
        if (!resized)
        {
            return;
        }

        (view.Width, view.Height) = (mode.Width, mode.Height);
        if ((view.Allocator ?? _allocator) is { } allocator)
        {
            if (view.Swapchain is null)
            {
                view.Swapchain = new Swapchain(allocator, mode.Width, mode.Height, _swapFormat, view.SwapModifiers ?? _swapModifiers);
            }
            else
            {
                view.Swapchain.Resize(mode.Width, mode.Height);
            }
        }
        else
        {
            view.Target?.Destroy();
            view.Target = new MemoryBuffer(mode.Width, mode.Height, DrmFormat.Xrgb8888);
        }

        Relayout();
        ReapplyPinnedGeometry();
    }

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

    private void SecondaryRepaint(OutputView view)
    {
        if (view.Swapchain is null || view.Swapchain.Acquire(out _) is not { } buffer)
        {
            return;
        }

        var replicating = view.ReplicaSource is not null;
        if (view.ReplicaSource is { } replicaSource)
        {
            if (!ReplicaBlit(replicaSource, buffer))
            {
                return;
            }
        }
        else
        {
            var box = _layout.BoxOf(view.Output);
            _scene.Root.SetPosition(-box.X, -box.Y);
            var rendered = _scene.Render(_renderer, buffer, SceneOptions(view.Output));
            _scene.Root.SetPosition(0, 0);
            if (!rendered)
            {
                return;
            }
        }

        _frameState.Clear();
        _frameState.SetBuffer(buffer);
        if (view.Output.Commit(_frameState))
        {
            view.Scheduler!.NotifyCommitted();
            view.LastPresentedBuffer = buffer;
        }

        _capture.NotifyDamaged(view.Output, new Box(0, 0, view.Width, view.Height));
        if (!replicating)
        {
            _scene.SendFrameDone((uint)Environment.TickCount);
        }
    }

    private bool ReplicaBlit(OutputView source, IBuffer target)
    {
        if ((source.LastPresentedBuffer ?? source.SceneOutput?.LastTarget) is not { } sourceBuffer)
        {
            return false;
        }

        if (_renderer.ImportTexture(sourceBuffer) is not { } texture)
        {
            return false;
        }

        try
        {
            var scale = Math.Min(
                (double)target.Width / sourceBuffer.Width, (double)target.Height / sourceBuffer.Height);
            var width = (int)Math.Round(sourceBuffer.Width * scale);
            var height = (int)Math.Round(sourceBuffer.Height * scale);
            var pass = _renderer.BeginBufferPass(target, new RenderPassOptions());
            pass.AddRect(new RenderColor(0f, 0f, 0f, 1f), new Box(0, 0, target.Width, target.Height));
            pass.AddTexture(texture, new TextureRenderOptions
            {
                DstBox = new Box((target.Width - width) / 2, (target.Height - height) / 2, width, height),
            });
            pass.Submit();
            return true;
        }
        finally
        {
            texture.Dispose();
        }
    }

    private static readonly double[] ScaleSteps = [1, 1.25, 1.5, 2];

    private void CycleScale()
    {
        var view = _views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output)
            ?? _views.FirstOrDefault();
        if (view is null)
        {
            return;
        }

        var index = Array.FindIndex(ScaleSteps, s => Math.Abs(s - view.Output.Scale) < 0.001);
        SetOutputScale(view, ScaleSteps[(index + 1) % ScaleSteps.Length]);
    }

    private void SetOutputScale(OutputView view, double scale)
    {
        using var state = new OutputState();
        if (!view.Output.Commit(state.SetScale(scale)))
        {
            _log.Warn($"scale {scale} refused by {view.Output.Name}");
            return;
        }

        BasinReport.Line($"SCALE {view.Output.Name} {view.Output.Scale}");
        Relayout();
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
            if (TouchesColor(entry) && _views.FirstOrDefault(v => v.Output == entry.Output) is { } colorView)
            {
                RefreshOutputColor(colorView);
            }
        }

        foreach (var entry in entries)
        {
            if (entry.ReplicationSourceUuid is not { } uuid ||
                _views.FirstOrDefault(v => v.Output == entry.Output) is not { } replicaView)
            {
                continue;
            }

            replicaView.ReplicaSource = uuid.Length > 0
                ? _views.FirstOrDefault(v =>
                    v != replicaView && Basin.Desktop.OutputUuid.For(v.Output) == uuid)
                : null;
            replicaView.Scheduler?.ScheduleRepaint();
        }

        foreach (var entry in entries)
        {
            if (_views.FirstOrDefault(v => v.Output == entry.Output) is { } view &&
                entry is { Enabled: true, Position: not null })
            {
                view.AutoLayout = false;
            }
        }

        Relayout();
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

        foreach (var view in _views)
        {
            if (!_layout.Contains(view.Output))
            {
                continue;
            }

            if (_fullRepaint)
            {
                view.Output.RequestFrame();
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

    private sealed class OutputView(OutputBase output, OutputGlobal global)
    {
        public OutputBase Output { get; } = output;

        public OutputGlobal Global { get; } = global;

        public MemoryBuffer? Target { get; set; }

        public Swapchain? Swapchain { get; set; }

        public SceneOutput? SceneOutput { get; set; }

        public OutputScheduler? Scheduler { get; set; }

        public bool AutoLayout { get; set; } = true;

        public bool FrameDonesPending { get; set; }

        public bool AutoVrrActive { get; set; }

        public OutputView? ReplicaSource { get; set; }

        public long Rendered { get; set; }

        public (ulong TimeNs, uint RefreshNs, ulong Sequence)? LastPresent { get; set; }

        public bool PresentDiscarded { get; set; }

        public IBuffer? LastPresentedBuffer { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public IAllocator? Allocator { get; set; }

        public ulong[]? SwapModifiers { get; set; }

        public bool IsSecondary { get; set; }

        public Basin.Capabilities.ImageDescription ColorDescription { get; set; } = Basin.Capabilities.ImageDescription.Srgb;

        public bool KmsColorRouted { get; set; }

        public Box UsableArea { get; set; }

        public ulong GroupId { get; set; }

        public Basin.Capabilities.WorkspaceSet<Workspace> Workspaces { get; } = new();

        public Workspace? Active
        {
            get => Workspaces.Active;
            set => Workspaces.Active = value;
        }
    }
}
