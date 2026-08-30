using Basin;
using Basin.Diagnostics;
using Basin.Effects;
using Basin.Scene;

namespace TinyComp;

internal sealed class PostEffects : IDisposable
{
    private static readonly AnimationDuration Crossfade = new(400);
    private static readonly AnimationDuration Rotation = new(250);

    private readonly IRenderer _renderer;
    private readonly BasinLogger _log;
    private readonly string _rendererName;
    private readonly List<IPostStage> _stages = [];

    private IPixelShader? _invertShader;
    private IPixelShader? _zoomShader;
    private IPixelShader? _colorBlindShader;
    private InvertStage? _invert;
    private MagnifierStage? _magnifier;
    private ZoomStage? _zoom;
    private ColorBlindnessStage? _colorBlind;
    private ShowPaintStage? _showPaint;
    private BlendChangesStage? _blend;
    private ScreenTransformAnimation? _transform;
    private bool _warned;

    public PostEffects(IRenderer renderer, string rendererName, BasinLogger log)
    {
        _renderer = renderer;
        _rendererName = rendererName;
        _log = log;
    }

    public IReadOnlyList<IPostStage> Stages => _stages;

    public MagnifierStage? Magnifier => _magnifier;

    public ZoomStage? Zoom => _zoom;

    public BlendChangesStage? Blend => _blend;

    public ScreenTransformAnimation? Transform => _transform;

    public bool Any => _stages.Count > 0;

    public void Configure(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _stages.Clear();

        foreach (var name in config.Post)
        {
            switch (name)
            {
                case "invert":
                    _invertShader ??= _renderer.CompilePixelShader(InvertShader.Source, InvertShader.Uniforms);
                    _invert ??= new InvertStage(_invertShader);
                    Add(_invert, name, _invertShader is not null);
                    break;

                case "magnify":
                    _magnifier ??= new MagnifierStage();
                    _magnifier.TargetZoom = 2.0;
                    Add(_magnifier, name, supported: true);
                    break;

                case "zoom":
                    _zoomShader ??= _renderer.CompilePixelShader(ZoomShader.Source, ZoomShader.Uniforms);
                    _zoom ??= new ZoomStage(_zoomShader, new ZoomOptions { MouseTracking = config.ZoomTracking });
                    Add(_zoom, name, _zoomShader is not null);
                    break;

                case "color-blindness":
                    _colorBlindShader ??= _renderer.CompilePixelShader(
                        ColorBlindnessShader.Source, ColorBlindnessShader.Uniforms);
                    _colorBlind ??= new ColorBlindnessStage(_colorBlindShader);
                    _colorBlind.Mode = config.ColorBlindness;
                    _colorBlind.Intensity = config.ColorBlindnessIntensity;
                    Add(_colorBlind, name, _colorBlind.IsSupported);
                    break;

                case "show-paint":
                    _showPaint ??= new ShowPaintStage();
                    Add(_showPaint, name, supported: true);
                    break;
            }
        }

        if (config.BlendChanges)
        {
            _blend ??= new BlendChangesStage(_renderer);
            _stages.Add(_blend);
        }

        if (config.ScreenTransform)
        {
            _transform ??= new ScreenTransformAnimation(_renderer);
            _stages.Add(_transform);
        }
    }

    public void Apply(SceneOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var stage in _stages)
        {
            output.AddPostStage(stage);
        }
    }

    public void Remove(SceneOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var stage in _stages)
        {
            _ = output.RemovePostStage(stage);
        }
    }

    public bool BeginCrossfade(IBuffer? previous, in FrameTick tick) =>
        previous is not null && _blend?.Begin(previous, tick, Crossfade) == true;

    public bool BeginRotation(IBuffer? previous, OutputTransform from, OutputTransform to, in FrameTick tick) =>
        previous is not null && _transform?.Begin(previous, from, to, tick, Rotation) == true;

    public bool Step(in FrameTick tick, int width, int height)
    {
        var running = false;
        if (_magnifier is { } magnifier)
        {
            running |= magnifier.Step(tick);
        }

        if (_zoom is { } zoom)
        {
            running |= zoom.Step(tick, width, height);
        }

        if (_blend is { IsRunning: true } blend)
        {
            running |= blend.Step(tick);
        }

        if (_transform is { IsRunning: true } transform)
        {
            running |= transform.Step(tick);
        }

        return running;
    }

    public void SetCursor(double x, double y, long nanos)
    {
        if (_magnifier is { } magnifier)
        {
            magnifier.CenterX = x;
            magnifier.CenterY = y;
        }

        _zoom?.SetCursor(x, y, nanos);
    }

    public void Dispose()
    {
        _blend?.Dispose();
        _transform?.Dispose();
        _invertShader?.Dispose();
        _zoomShader?.Dispose();
        _colorBlindShader?.Dispose();
        _stages.Clear();
    }

    private void Add(IPostStage stage, string name, bool supported)
    {
        if (supported)
        {
            _stages.Add(stage);
            return;
        }

        if (!_warned)
        {
            _warned = true;
            _log.Warn($"{_rendererName} compiles no pixel shader dialect; the {name} post stage is ignored");
        }
    }
}
