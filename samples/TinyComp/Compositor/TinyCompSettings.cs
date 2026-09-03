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
    private void Reload()
    {
        var loaded = Config.Load(_configPath, _log, out var fatal);
        if (fatal is not null)
        {
            _log.Warn($"reload failed, keeping the running config: {fatal}");
            BasinReport.Line("RELOAD failed");
            return;
        }

        if (loaded.Shaders.Count > 0 && !Basin.Rashader.RashaderLibrary.IsAvailable(out var shaderWhy))
        {
            _log.Error($"reload failed, keeping the running config: [effects] shader: {shaderWhy}");
            BasinReport.Line("RELOAD failed");
            return;
        }

        var restart = new List<string>();

        void Restarts(string key, bool changed)
        {
            if (changed && loaded.FromFile.Contains(key) && !_config.FromFlags.Contains(key))
            {
                restart.Add(key);
            }
        }

        Restarts("renderer", loaded.Renderer != _config.Renderer);
        Restarts("outputs", loaded.Outputs != _config.Outputs);
        Restarts("frames", loaded.Frames != _config.Frames);
        Restarts("full_repaint", loaded.FullRepaint != _fullRepaint);
        Restarts("color.source", loaded.ColorSource != _colorSource);
        Restarts("color.icc", loaded.IccProfile != _iccProfile);
        Restarts("color.hdr", loaded.Hdr != _hdr);
        Restarts("hypr.enable", loaded.HyprEnabled != _config.HyprEnabled);
        Restarts("hypr.input_capture", loaded.HyprInputCapture != _config.HyprInputCapture);
        Restarts("hypr.ctm", loaded.HyprCtm != _config.HyprCtm);

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

        _config = loaded;

        _useTransactions = loaded.Transactions;
        _offload = loaded.Offload;
        _damageTint = loaded.DamageTint;
        _driver.AllowPlaneOffload = _offload;
        _driver.DebugDamageTint = _damageTint;
        ApplyEffectShaders();

        _scales = loaded.Scales;
        _driver.Scales = loaded.Scales;
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            var scale = ReloadScaleFor(i, view.Output);
            if (scale is { } wanted && wanted != view.Output.Scale)
            {
                SetOutputScale(view, wanted);
            }
        }

        ApplyNightLight(loaded.NightLight);
        ApplyFrameStyle(loaded.FrameStyle);
        ApplyCornerRadius(loaded.CornerRadius);
        ApplyPostStages(loaded);
        ApplyScreenShader(loaded);
        ApplyEffectSettings(loaded);
        _hyprShortcuts.Configure(loaded);

        foreach (var view in Views)
        {
            if (view.Scene is not null && view.LastPresentedBuffer is { } presented)
            {
                _ = _post.BeginCrossfade(presented, EffectTick());
            }

            view.Scheduler?.ScheduleRepaint();
        }

        BasinReport.Line(
            $"RELOAD bindings={loaded.Bindings.Count} rules={loaded.Rules.Count}"
            + " rules-apply-to-windows-mapped-after-this"
            + (restart.Count == 0 ? string.Empty : $" restart-required={string.Join(',', restart)}"));
    }

    private double? ReloadScaleFor(int index, IOutput output) =>
        _scales.Length > 0 ? _scales[Math.Min(index, _scales.Length - 1)]
        : _config.OutputSettingFor(output.Name)?.Scale
            ?? (output is Basin.Backend.Wayland.WaylandOutput hosted ? hosted.HostScale : null);

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
        if (!wanted)
        {
            _effects.Dim = null;
            foreach (var window in _windows)
            {
                window.SetDimmed(false);
            }

            foreach (var xwindow in _xwindows)
            {
                xwindow.SetDimmed(false);
            }

            return;
        }

        if (_effects.Dim is not null)
        {
            return;
        }

        if (_dimShader is null && !_dimShaderTried)
        {
            _dimShaderTried = true;
            _dimShader = _renderer.CompilePixelShader(
                Basin.Effects.DimShader.Source, Basin.Effects.DimShader.Uniforms);
            if (_dimShader is null)
            {
                _log.Warn($"{_rendererName} compiles no pixel shader dialect; dim_inactive is ignored");
            }
        }

        if (_dimShader is null)
        {
            return;
        }

        _effects.Dim = new Basin.Effects.DimInactiveEffect(_dimShader);
        _effects.DimChanged = dim => _dimShader?.SetUniforms([(float)dim]);
        _dimShader.SetUniforms([(float)_effects.Dim.Dim]);
        RefreshDim();
    }

    private void ApplyDropShadows(bool wanted)
    {
        if (!wanted)
        {
            _shadowTexture?.Dispose();
            _shadowTexture = null;
            foreach (var window in _windows)
            {
                window.SetShadow(null);
            }

            foreach (var xwindow in _xwindows)
            {
                xwindow.SetShadow(null);
            }

            return;
        }

        _shadowTexture ??= Basin.Effects.DropShadowTexture.Build(new Basin.Effects.DropShadowOptions(), 1.0);
        foreach (var window in _windows)
        {
            window.SetShadow(_shadowTexture);
        }

        foreach (var xwindow in _xwindows)
        {
            xwindow.SetShadow(_shadowTexture);
        }
    }

    internal void RefreshDim()
    {
        if (_effects.Dim is null)
        {
            return;
        }

        var anyInactive = false;
        foreach (var window in _windows)
        {
            var inactive = window != _focused;
            window.SetDimmed(inactive);
            anyInactive |= inactive;
        }

        foreach (var xwindow in _xwindows)
        {
            var inactive = xwindow != _focusedX;
            xwindow.SetDimmed(inactive);
            anyInactive |= inactive;
        }

        _effects.FadeDim(anyInactive);
    }
}
