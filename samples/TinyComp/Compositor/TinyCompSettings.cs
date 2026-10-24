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
    private List<string> _textureErrors = [];

    private string _inputRuleReport = string.Empty;

    private void Reload()
    {
        var loaded = Config.Load(_configPath ?? Config.DefaultPath(), _log, out var fatal);
        if (fatal is not null)
        {
            _log.Warn($"reload: running config kept, {fatal}");
            _report.Line("RELOAD failed");
            return;
        }

        if (loaded.Shaders.Count > 0 && !Basin.Rashader.RashaderLibrary.IsAvailable(out var shaderWhy))
        {
            _log.Warn($"reload: running config kept, [effects] shader: {shaderWhy}");
            _report.Line("RELOAD failed");
            return;
        }

        _panels = Basin.Color.PanelTable.Load(_log);
        foreach (var view in Views)
        {
            if (view.Output is Basin.Backend.Drm.DrmOutput panelOutput)
            {
                _colorConfiguration?.SetPanel(panelOutput, _panels.Match(panelOutput));
            }
        }

        var restart = CarryOver(loaded);
        _gridPreset = null;
        _gridPresetName = null;
        var metacityFailure = ApplyConfig(loaded, crossfade: true);
        _driver.ConfigureHandoff(loaded.Handoff.Transition == Basin.Config.HandoffTransitionKind.Crossfade, loaded.Handoff.DurationMs, loaded.Handoff.HoldLimitMs);
        var textureErrors = _textureErrors;
        var inputRejections = _inputRuleReport;

        _report.Line(
            $"RELOAD bindings={loaded.Bindings.Count} rules={loaded.Rules.Count}"
            + " rules-apply-to-windows-mapped-after-this"
            + (restart.Count == 0 ? string.Empty : $" restart-required={string.Join(',', restart)}")
            + (metacityFailure is null ? string.Empty : " metacity=kept")
            + (_msstylesKept ? " msstyles=kept" : string.Empty)
            + (textureErrors.Count == 0 ? string.Empty : " " + string.Join(' ', textureErrors))
            + $" input-rules={loaded.InputRules.Count}{inputRejections}"
            + ConfigSummary(loaded));
        ReloadSettingsDraft();
    }

    private static string ConfigSummary(Config loaded) =>
        $" canvas={(loaded.CanvasAnywhere ? "on" : "off")} canvas-edge-scale={loaded.Canvas.EdgeScaleValue:F3}"
        + $" canvas-sides={loaded.Canvas.SideNames} canvas-corner={loaded.Canvas.CornerName} canvas-corner-radius={loaded.Canvas.CornerRadiusValue:F2}"
        + $" canvas-window={loaded.Canvas.WindowName} canvas-min-scale={loaded.Canvas.MinScaleValue:F2} canvas-scale-reach={loaded.Canvas.ScaleReachValue:F2}"
        + $" canvas-shelf={loaded.Canvas.ShelfFraction:F2} canvas-shelf-scale={loaded.Canvas.ShelfScaleValues.Names} canvas-shelf-min-scale={loaded.Canvas.ShelfMinScaleValue:F2} canvas-shelf-shape={loaded.Canvas.ShapeName} canvas-slope-window={loaded.Canvas.OnSlopeName} canvas-drag={loaded.Canvas.DragName}"
        + $" overview={(loaded.Overview.Enabled ? "on" : "off")} overview-scale={loaded.Overview.ScaleValue:F2} overview-hot-corner={loaded.Overview.HotCornerName} overview-wall={loaded.Overview.WallName}"
        + GridSummary("canvas", loaded.Canvas.GridEffectsValue)
        + GridSummary("overview", loaded.Overview.TerraceValue.GridEffectsValue)
        + $" physics-engine={loaded.Physics.Engine}";

    private static string GridSummary(string table, GridEffectsSetting effects) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $" {table}-grid-glow={effects.GlowValue:F2} {table}-grid-path={effects.GlowPathName}"
        + $" {table}-grid-highlight={effects.HighlightDraggedValue:F2}/{effects.HighlightFocusedValue:F2}/{effects.HighlightWindowsValue:F2}/{effects.HighlightPointerValue:F2}"
        + $" {table}-grid-density={(effects.DensityValue ? "on" : "off")} {table}-grid-pinch={effects.PinchValue:F2}");

    private List<string> CarryOver(Config loaded)
    {
        var restart = new List<string>();

        void Restarts(string key, bool changed)
        {
            if (changed && loaded.FromFile.Contains(key) && !_config.FromFlags.Contains(key))
            {
                restart.Add(key);
            }
        }

        Restarts("renderer", loaded.Renderer != _config.Renderer);
        if (loaded.Handoff.Mode != _config.Handoff.Mode)
        {
            restart.Add("handoff.mode");
        }

        Restarts("outputs", loaded.Outputs != _config.Outputs);
        Restarts("frames", loaded.Frames != _config.Frames);
        Restarts("full_repaint", loaded.FullRepaint != _fullRepaint);
        Restarts("color.source", loaded.ColorSource != _colorSource);
        Restarts("color.icc", loaded.IccProfile != _iccProfile);
        Restarts("color.hdr", loaded.Hdr != _hdr);
        Restarts("hypr.enable", loaded.HyprEnabled != _config.HyprEnabled);
        Restarts("hypr.input_capture", loaded.HyprInputCapture != _config.HyprInputCapture);
        Restarts("hypr.ctm", loaded.HyprCtm != _config.HyprCtm);
        Restarts("xwayland.low_density", loaded.XWaylandLowDensity != _config.XWaylandLowDensity);

        loaded.Renderer = _config.Renderer;
        loaded.Outputs = _config.Outputs;
        loaded.Frames = _config.Frames;
        loaded.FullRepaint = _fullRepaint;
        loaded.ColorSource = _colorSource;
        loaded.IccProfile = _iccProfile;
        loaded.Hdr = _hdr;
        loaded.HyprEnabled = _config.HyprEnabled;
        loaded.HyprInputCapture = _config.HyprInputCapture;
        loaded.HyprCtm = _config.HyprCtm;
        loaded.XWaylandLowDensity = _config.XWaylandLowDensity;
        foreach (var key in _config.FromFlags)
        {
            loaded.FromFlags.Add(key);
        }

        if (loaded.FromFlags.Contains("offload"))
        {
            loaded.Offload = _offload;
        }

        if (loaded.FromFlags.Contains("damage_tint"))
        {
            loaded.DamageTint = _damageTint;
        }

        if (loaded.FromFlags.Contains("scale"))
        {
            loaded.Scales = _scales;
        }

        return restart;
    }

    private void ApplyXWaylandScale() => _xwaylandModule?.Configure(_config.XWaylandScale, _config.XWaylandDpiBuckets);

    private string? ApplyConfig(Config next, bool crossfade)
    {
        _config.CursorHideAfterMs = next.CursorHideAfterMs;
        _config.CursorParkPointer = next.CursorParkPointer;
        ApplyCursorIdle();
        var background = _config.Background != next.Background;
        var glassBefore = _config;
        _config = next;
        if (!next.HideCursorOnTouch)
        {
            RevealCursorForPointer();
        }

        ApplyTouchSettings();
        ApplyXWaylandScale();

        if (background)
        {
            _driver.Background = next.Background;
            _capture.Background = next.Background;
            foreach (var view in Views)
            {
                view.Scene?.Ring.AddWhole();
            }
        }

        if (_inputConfigurator.IsAttached)
        {
            _inputRuleReport = ApplyInputRules(next.InputRules);
        }

        _useTransactions = next.Transactions;
        _offload = next.Offload;
        _damageTint = next.DamageTint;
        _driver.AllowPlaneOffload = _offload;
        _driver.VrrPacing = next.VrrPacing;
        _driver.AllowTearing = next.AllowTearing;
        _driver.DebugDamageTint = _damageTint;
        ApplyEffectShaders();

        _scales = next.Scales;
        _driver.Scales = next.Scales;
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            var scale = ReloadScaleFor(i, view.Output);
            if (scale is { } wanted && wanted != view.Output.Scale)
            {
                SetOutputScale(view, wanted);
            }
        }

        ApplyNightLight(next.NightLight);
        ApplyColorLooks();
        var chromeBefore = (_frameStyle, _frameTheme, _metacity, _msstyles, _quillTheme);
        var metacityFailure = ApplyFrameFont(next) ?? ApplyMetacity(next);
        _msstylesKept = ApplyMsStyles(next) is not null;
        ApplyQuill(next);
        ApplyGlass(glassBefore, next);
        ApplyFrameStyle(next.FrameStyle);
        if (chromeBefore != (_frameStyle, _frameTheme, _metacity, _msstyles, _quillTheme))
        {
            RebuildHostChrome();
        }

        ApplyCornerRadius(next.CornerRadius);
        ApplyPostStages(next);
        ApplyScreenShader(next);
        ApplyEffectSettings(next);
        ApplyInspectorSettings(next);
        ConfigureShortcuts(next);
        _textureErrors = LoadStepTextures(next, reload: true);
        _canvasOverride = null;
        _canvasClosing = false;
        ConfigureOverviewTriggers();
        CloseRefusedOverviews();
        LayoutCanvasAll();
        ApplyPhysicsSettings();

        foreach (var view in Views)
        {
            if (crossfade && view.Scene is not null && view.LastPresentedBuffer is { } presented)
            {
                _ = _post.BeginCrossfade(presented, EffectTick());
            }

            view.Scheduler?.ScheduleRepaint();
        }

        return metacityFailure;
    }

    private double? ReloadScaleFor(int index, IOutput output) =>
        _scales.Length > 0 ? _scales[Math.Min(index, _scales.Length - 1)]
        : _config.OutputSettingFor(output.Name)?.Scale
            ?? (output is Basin.Backend.Wayland.WaylandOutput hosted ? hosted.HostScale : null);

    private string? ApplyMetacity(Config loaded)
    {
        if (loaded.FrameStyle != FrameStyle.Metacity && !_windows.Any(w => w.Rule?.FrameStyle == FrameStyle.Metacity)
            && !_xwindows.Any(w => w.Rule?.FrameStyle == FrameStyle.Metacity))
        {
            return null;
        }

        if (_metacity is { } current && current.Matches(loaded))
        {
            return null;
        }

        MetacityFrames replacement;
        try
        {
            replacement = MetacityFrames.Load(loaded, FrameThemeOrLoad());
        }
        catch (Basin.Frames.Metacity.MetacityThemeException e)
        {
            _log.Warn(e, $"reload: running metacity theme kept");
            return e.Message;
        }

        var previous = _metacity;
        _metacity = replacement;
        foreach (var window in _windows)
        {
            if ((window.Rule?.FrameStyle ?? _frameStyle) == FrameStyle.Metacity)
            {
                window.RebuildFrame();
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if ((xwindow.Rule?.FrameStyle ?? _frameStyle) == FrameStyle.Metacity)
            {
                xwindow.RebuildFrame();
            }
        }

        previous?.Dispose();
        return null;
    }

    private string? ApplyMsStyles(Config loaded)
    {
        if (loaded.FrameStyle != FrameStyle.MsStyles && !_windows.Any(w => w.Rule?.FrameStyle == FrameStyle.MsStyles)
            && !_xwindows.Any(w => w.Rule?.FrameStyle == FrameStyle.MsStyles))
        {
            return null;
        }

        if (_msstyles is { } current && current.Matches(loaded))
        {
            return null;
        }

        MsStylesFrames replacement;
        try
        {
            replacement = MsStylesFrames.Load(loaded, MsStylesIcon);
        }
        catch (MsStylesFrameException e)
        {
            _log.Warn(e, $"reload: running msstyles theme kept");
            return e.Message;
        }

        var previous = _msstyles;
        _msstyles = replacement;
        _report.Line(replacement.Describe());
        foreach (var window in _windows)
        {
            if ((window.Rule?.FrameStyle ?? _frameStyle) == FrameStyle.MsStyles)
            {
                window.RebuildFrame();
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if ((xwindow.Rule?.FrameStyle ?? _frameStyle) == FrameStyle.MsStyles)
            {
                xwindow.RebuildFrame();
            }
        }

        previous?.Dispose();
        return null;
    }

    private string? ApplyFrameFont(Config loaded)
    {
        if (_frameTheme is not { } previous || previous.FontSize == (float)loaded.FontSize)
        {
            return null;
        }

        var theme = new FrameTheme((float)loaded.FontSize);
        MetacityFrames? metacity = null;
        if (_metacity is not null)
        {
            try
            {
                metacity = MetacityFrames.Load(loaded, theme);
            }
            catch (Basin.Frames.Metacity.MetacityThemeException e)
            {
                theme.Dispose();
                _log.Warn(e, $"reload: running font size and metacity theme kept");
                return e.Message;
            }
        }

        var previousMetacity = _metacity;
        var previousFlat = _flatTheme;
        _frameTheme = theme;
        _flatTheme = previousFlat is null ? null : FlatThemeOf(theme);
        _metacity = metacity;
        foreach (var window in _windows)
        {
            window.RebuildFrame();
        }

        foreach (var xwindow in _xwindows)
        {
            xwindow.RebuildFrame();
        }

        previousMetacity?.Dispose();
        previousFlat?.Dispose();
        previous.Dispose();
        return null;
    }

    private void ApplyQuill(Config loaded)
    {
        if (_quillTheme is not { } theme)
        {
            return;
        }

        var wanted = QuillThemeOf(loaded);
        if (theme.CornerRadius == wanted.CornerRadius && theme.FontSize == wanted.FontSize
            && theme.Frosted == wanted.Frosted && theme.FrostAlpha == wanted.FrostAlpha
            && theme.Text.Equals(wanted.Text))
        {
            return;
        }

        _quillTheme = wanted;
        foreach (var window in _windows)
        {
            if ((window.Rule?.FrameStyle ?? _frameStyle) == FrameStyle.Quill)
            {
                window.RebuildFrame();
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if ((xwindow.Rule?.FrameStyle ?? _frameStyle) == FrameStyle.Quill)
            {
                xwindow.RebuildFrame();
            }
        }
    }

    private void ApplyGlass(Config before, Config loaded)
    {
        if (_glassEffect is { } glass)
        {
            var options = GlassOptionsOf(loaded);
            if (glass.Options != options)
            {
                glass.Options = options;
                foreach (var view in Views)
                {
                    view.Scene?.Ring.AddWhole();
                    view.Scheduler?.ScheduleRepaint();
                }
            }
        }

        if ((before.GlassCornerRadius, before.GlassShadow, before.GlassGlow)
            != (loaded.GlassCornerRadius, loaded.GlassShadow, loaded.GlassGlow))
        {
            _glassShadowTexture?.Dispose();
            _glassShadowTexture = null;
            _glassGlowTexture?.Dispose();
            _glassGlowTexture = null;
            RefreshShadows();
        }

        if ((before.GlassPalette, before.GlassCornerRadius, before.GlassFontSize, before.GlassBevel, before.GlassAnimationMs, before.GlassClientGlass, before.GlassInset, before.GlassContentRadius)
            == (loaded.GlassPalette, loaded.GlassCornerRadius, loaded.GlassFontSize, loaded.GlassBevel, loaded.GlassAnimationMs, loaded.GlassClientGlass, loaded.GlassInset, loaded.GlassContentRadius))
        {
            return;
        }

        foreach (var window in _windows)
        {
            if ((window.Rule?.FrameStyle ?? _frameStyle) == FrameStyle.Glass)
            {
                window.RebuildFrame();
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if ((xwindow.Rule?.FrameStyle ?? _frameStyle) == FrameStyle.Glass)
            {
                xwindow.RebuildFrame();
            }
        }
    }

    private void ApplyFrameStyle(FrameStyle style)
    {
        if (style == _frameStyle)
        {
            return;
        }

        _frameStyle = style;
        foreach (var window in _windows)
        {
            if (window.Rule?.FrameStyle is null)
            {
                window.RebuildFrame();
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (xwindow.Rule?.FrameStyle is null)
            {
                xwindow.RebuildFrame();
            }
        }
    }

    private void AddPostStage(OutputView view)
    {
        if (view.Scene is { } output)
        {
            _post.Apply(output);
            _shader.Apply(output);
        }
    }

    private void ApplyPostStages(Config config)
    {
        foreach (var view in Views)
        {
            if (view.Scene is { } output)
            {
                _post.Remove(output);
            }
        }

        _post.Configure(config);

        foreach (var view in Views)
        {
            if (view.Scene is { } output)
            {
                _post.Apply(output);
            }
        }
    }

    private void ApplyScreenShader(Config config)
    {
        _shader.Configure(config);
        foreach (var view in Views)
        {
            if (view.Scene is { } output)
            {
                _shader.Apply(output);
            }
        }
    }

    private void ApplyEffectSettings(Config config)
    {
        _effects.WobblyEnabled = config.Wobbly;
        _effects.OpenKind = config.OpenAnimation;
        _effects.CloseKind = config.CloseAnimation;
        _effects.MinimizeKind = config.MinimizeAnimation;
        _effects.SwitcherEnabled = config.Switcher;
        _effects.HighlightEnabled = config.Highlight;
        _effects.SlideBackEnabled = config.SlideBack;
        _effects.StretchEnabled = config.Stretch;
        _effects.NotificationsEnabled = config.Notifications;
        ApplyBlurKernel(config);

        if (!config.Highlight)
        {
            _effects.ClearHighlights();
        }

        ApplyDim(config.DimInactive);
        ApplyDropShadows(config.DropShadow);

        if (config.MouseClick || config.MouseMark || config.TrackMouse || config.TouchPoints
            || config.SystemBell || config.ShakeCursor
            || config.StartupFeedback != Basin.Effects.StartupFeedbackKind.None)
        {
            _feedback ??= new FeedbackEffects(_layers.Feedback)
            {
                MagnificationChanged = magnification => _cursor.Magnification = magnification,
            };
            _feedback.Configure(config);
        }
        else if (_feedback is { } feedback)
        {
            feedback.Dispose();
            _feedback = null;
            _cursor.Magnification = 1.0;
        }
    }

    private void ApplyDim(bool wanted)
    {
        var options = _config.DimOptions;
        if (wanted && _effects.Dim is { } running && running.Options == options)
        {
            return;
        }

        ClearDim();
        if (!wanted)
        {
            return;
        }

        _dimOverview = false;
        _effects.StartDim(new Basin.Effects.DimInactiveEffect<object>(options) { Target = new DimTarget(this) });
        RefreshDim();
    }

    private void ClearDim()
    {
        if (_effects.Dim is null)
        {
            return;
        }

        foreach (var window in _windows)
        {
            window.ApplyDim(1f, 1f);
        }

        foreach (var xwindow in _xwindows)
        {
            xwindow.ApplyDim(1f, 1f);
        }

        foreach (var (layer, _) in _layerDriver.Surfaces)
        {
            ApplyLayerDim(layer, 1f, 1f);
        }

        _effects.StopDim();
    }

    private void ApplyDropShadows(bool wanted)
    {
        if (!wanted)
        {
            _shadowTexture?.Dispose();
            _shadowTexture = null;
            RefreshShadows();
            return;
        }

        _shadowTexture ??= Basin.Effects.DropShadowTexture.Build(new Basin.Effects.DropShadowOptions(), 1.0);
        RefreshShadows();
    }

    private void RefreshShadows()
    {
        foreach (var window in _windows)
        {
            window.RefreshShadow();
        }

        foreach (var xwindow in _xwindows)
        {
            xwindow.RefreshShadow();
        }
    }

    internal (Basin.Effects.DropShadowTexture? Shadow, Basin.Effects.DropShadowTexture? Glow) ShadowsFor(FrameStyle style, bool framed)
    {
        if (framed && style == FrameStyle.Glass && _config.GlassShadow && _glassEffect is not null && _quillHost is not null)
        {
            var radius = Math.Max(0, _config.GlassCornerRadius);
            _glassShadowTexture ??= Basin.Effects.DropShadowTexture.Build(
                new Basin.Effects.DropShadowOptions { CornerRadius = radius, Overlap = 1 }, 1.0);
            var glow = _config.GlassGlow;
            if (glow >> 24 != 0)
            {
                _glassGlowTexture ??= Basin.Effects.DropShadowTexture.Build(
                    new Basin.Effects.DropShadowOptions
                    {
                        Primary = new Basin.Effects.DropShadowLayer(0, 0, 32, (glow >> 24) / 255.0),
                        Secondary = new Basin.Effects.DropShadowLayer(0, 0, 0, 0),
                        OffsetY = 0,
                        CornerRadius = radius,
                        Overlap = 1,
                        Color = new RenderColor(((glow >> 16) & 0xFF) / 255f, ((glow >> 8) & 0xFF) / 255f, (glow & 0xFF) / 255f, 1f),
                    },
                    1.0);
            }

            return (_glassShadowTexture, glow >> 24 != 0 ? _glassGlowTexture : null);
        }

        return (_config.DropShadow ? _shadowTexture : null, null);
    }

    internal void RefreshDim()
    {
        if (_effects.Dim is not { } dim)
        {
            return;
        }

        foreach (var window in _windows)
        {
            if (window.SceneSurface is null)
            {
                continue;
            }

            var facts = DimFacts(window.Toplevel.Surface, window.Toplevel.Parent is null ? DimInactiveWindowKind.Normal : DimInactiveWindowKind.Dialog)
                with { KeepAbove = window.Above, FullScreen = window.IsFullscreen };
            if (!dim.Update(window, facts))
            {
                dim.Add(window, facts);
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (xwindow.SceneSurface.IsDestroyed)
            {
                continue;
            }

            var kind = xwindow.XWin.OverrideRedirect
                ? DimInactiveWindowKind.Unmanaged
                : DimInactiveWindow.KindOfWindowType(xwindow.XWin.WindowType);
            var facts = DimFacts(xwindow.XWin.Surface, kind) with { FullScreen = xwindow.Fullscreen };
            if (!dim.Update(xwindow, facts))
            {
                dim.Add(xwindow, facts);
            }
        }

        foreach (var (layer, scene) in _layerDriver.Surfaces)
        {
            if (scene is null)
            {
                continue;
            }

            var facts = new DimInactiveWindow(layer.Layer switch
            {
                LayerKind.Background => DimInactiveWindowKind.Desktop,
                LayerKind.Bottom or LayerKind.Top => DimInactiveWindowKind.Panel,
                _ => DimInactiveWindowKind.Popup,
            });
            if (!dim.Update(layer, facts))
            {
                dim.Add(layer, facts);
            }
        }

        dim.Activate(_focused is not null ? _focused : _focusedX);
    }

    private void ApplyLayerDim(LayerSurface layer, float brightness, float saturation)
    {
        if (_layerDriver.SceneOf(layer) is { } scene)
        {
            scene.Content.Brightness = brightness;
            scene.Content.Saturation = saturation;
        }
    }

    private DimInactiveWindow DimFacts(Surface? surface, DimInactiveWindowKind kind)
    {
        if (surface is not null && _toplevels.TryFind(surface, out var info))
        {
            return new DimInactiveWindow(kind, DimInactiveWindow.GroupOf(_toplevels, info));
        }

        return new DimInactiveWindow(kind);
    }

    private sealed class DimTarget(TinyComp comp) : IDimInactiveTarget<object>
    {
        public void Apply(object window, float brightness, float saturation)
        {
            switch (window)
            {
                case Window xdg:
                    xdg.ApplyDim(brightness, saturation);
                    break;
                case XWindow x11:
                    x11.ApplyDim(brightness, saturation);
                    break;
                case LayerSurface layer:
                    comp.ApplyLayerDim(layer, brightness, saturation);
                    break;
            }
        }
    }
}
