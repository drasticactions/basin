using Basin;
using Basin.Effects;
using Basin.Scene;

namespace PlasmaHost;

internal sealed class PlasmaHostStages : IDisposable
{
    private readonly IRenderer _renderer;
    private readonly List<SceneOutput> _outputs = [];
    private readonly List<IPixelShader> _shaders = [];
    private readonly List<IPostStage> _ordered = [];
    private readonly List<IPostStage> _next = [];
    private readonly List<IPostStage> _wanted = [];
    private readonly Dictionary<SceneOutput, BlendChangesStage> _blends = [];
    private readonly Dictionary<SceneOutput, List<IPostStage>> _live = [];
    private readonly bool _blendChanges;

    public PlasmaHostStages(KwinEffectsConfig config, IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;

        if (config.IsEnabled("invert", false))
        {
            Invert = new InvertStage(Compile(InvertShader.Source, InvertShader.Uniforms));
        }

        if (config.IsEnabled("colorblindnesscorrection", false))
        {
            ColorBlindness = new ColorBlindnessStage(
                Compile(ColorBlindnessShader.Source, ColorBlindnessShader.Uniforms))
            {
                Mode = (ColorBlindnessMode)config.Integer("colorblindnesscorrection", "Mode", 0),
                Intensity = config.Number("colorblindnesscorrection", "Intensity", 1.0),
            };
        }

        if (config.IsEnabled("magnifier", false))
        {
            Magnifier = new MagnifierStage(new MagnifierOptions
            {
                Width = config.Integer("magnifier", "Width", 200),
                Height = config.Integer("magnifier", "Height", 200),
                ZoomFactor = config.Number("zoom", "ZoomFactor", 1.2),
                InitialZoom = config.Number("zoom", "InitialZoom", 1.0),
            });
        }

        if (config.IsEnabled("zoom", true))
        {
            Zoom = new ZoomStage(
                Compile(ZoomShader.Source, ZoomShader.Uniforms),
                new ZoomOptions
                {
                    ZoomFactor = config.Number("zoom", "ZoomFactor", 1.2),
                    InitialZoom = config.Number("zoom", "InitialZoom", 1.0),
                    MouseTracking = (ZoomTracking)config.Integer("zoom", "MouseTracking", 0),
                    FocusTracking = config.Boolean("zoom", "EnableFocusTracking", false),
                    TextCaretTracking = config.Boolean("zoom", "EnableTextCaretTracking", false),
                    FocusDelayMillis = config.Integer("zoom", "FocusDelay", 350),
                    MoveFactor = config.Number("zoom", "MoveFactor", 20.0),
                    PixelGridZoom = config.Number("zoom", "PixelGridZoom", 15.0),
                    UsePatternUpscaler = config.Boolean("zoom", "UsePatternUpscaler", true),
                });
        }

        if (config.IsEnabled("showpaint", false))
        {
            ShowPaint = new ShowPaintStage();
        }

        _blendChanges = config.IsEnabled("blendchanges", true);

        if (config.IsEnabled("screentransform", true))
        {
            ScreenTransform = new ScreenTransformAnimation(renderer);
        }

        BuildOrder();
    }

    public InvertStage? Invert { get; }

    public ColorBlindnessStage? ColorBlindness { get; }

    public MagnifierStage? Magnifier { get; }

    public ZoomStage? Zoom { get; }

    public ShowPaintStage? ShowPaint { get; }

    public bool BlendsChanges => _blendChanges;

    public ScreenTransformAnimation? ScreenTransform { get; }

    public bool Any =>
        Invert is not null || ColorBlindness is not null || Magnifier is not null ||
        Zoom is not null || ShowPaint is not null || _blendChanges ||
        ScreenTransform is not null;

    public bool IsBlending
    {
        get
        {
            foreach (var blend in _blends.Values)
            {
                if (blend.IsRunning)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public BlendChangesStage? BlendFor(SceneOutput output) => _blends.GetValueOrDefault(output);

    public bool NeedsFullRepaint(SceneOutput output) =>
        ScreenTransform is { IsRunning: true } || _blends.GetValueOrDefault(output) is { IsRunning: true };

    public void Attach(SceneOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _outputs.Add(output);
        _live[output] = [];
        if (_blendChanges)
        {
            _blends[output] = new BlendChangesStage(_renderer);
        }

        Sync();
    }

    public void Detach(SceneOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _outputs.Remove(output);
        if (_live.Remove(output, out var stages))
        {
            foreach (var stage in stages)
            {
                output.RemovePostStage(stage);
            }
        }

        if (_blends.Remove(output, out var blend))
        {
            blend.Dispose();
        }
    }

    public bool Step(in FrameTick tick, double cursorX, double cursorY, int width, int height)
    {
        var running = false;
        if (Magnifier is { } magnifier)
        {
            magnifier.CenterX = cursorX;
            magnifier.CenterY = cursorY;
            running |= magnifier.Step(tick);
        }

        if (Zoom is { } zoom)
        {
            zoom.SetCursor(cursorX, cursorY, tick.TargetPresentNanos);
            running |= zoom.Step(tick, width, height);
        }

        foreach (var blend in _blends.Values)
        {
            running |= blend.Step(tick);
        }

        if (ScreenTransform is { } transform)
        {
            running |= transform.Step(tick);
        }

        running |= ShowPaint is not null;
        Sync();
        return running;
    }

    private void Sync()
    {
        _next.Clear();
        for (var i = 0; i < _ordered.Count; i++)
        {
            if (Wanted(_ordered[i]))
            {
                _next.Add(_ordered[i]);
            }
        }

        foreach (var output in _outputs)
        {
            FillFor(output);
            var live = _live[output];
            if (Same(live, _wanted))
            {
                continue;
            }

            foreach (var stage in live)
            {
                output.RemovePostStage(stage);
            }

            live.Clear();
            live.AddRange(_wanted);
            foreach (var stage in live)
            {
                output.AddPostStage(stage);
            }
        }
    }

    private void FillFor(SceneOutput output)
    {
        _wanted.Clear();
        var blend = _blends.GetValueOrDefault(output);
        var placed = blend is not { IsRunning: true };
        for (var i = 0; i < _next.Count; i++)
        {
            if (!placed && !ReferenceEquals(_next[i], ScreenTransform))
            {
                _wanted.Add(blend!);
                placed = true;
            }

            _wanted.Add(_next[i]);
        }

        if (!placed)
        {
            _wanted.Add(blend!);
        }
    }

    private static bool Wanted(IPostStage stage) => stage switch
    {
        ZoomStage zoom => zoom.IsActive,
        MagnifierStage magnifier => magnifier.IsActive,
        ScreenTransformAnimation transform => transform.IsRunning,
        _ => true,
    };

    private static bool Same(List<IPostStage> live, List<IPostStage> wanted)
    {
        if (live.Count != wanted.Count)
        {
            return false;
        }

        for (var i = 0; i < live.Count; i++)
        {
            if (!ReferenceEquals(live[i], wanted[i]))
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        foreach (var blend in _blends.Values)
        {
            blend.Dispose();
        }

        _blends.Clear();
        _live.Clear();
        ScreenTransform?.Dispose();
        foreach (var shader in _shaders)
        {
            shader.Dispose();
        }

        _shaders.Clear();
        _outputs.Clear();
    }

    private void BuildOrder()
    {
        foreach (var stage in Ordered())
        {
            _ordered.Add(stage);
        }
    }

    private IEnumerable<IPostStage> Ordered()
    {
        if (ScreenTransform is { } transform)
        {
            yield return transform;
        }

        if (Zoom is { } zoom)
        {
            yield return zoom;
        }

        if (Magnifier is { } magnifier)
        {
            yield return magnifier;
        }

        if (ColorBlindness is { } colorBlindness)
        {
            yield return colorBlindness;
        }

        if (Invert is { } invert)
        {
            yield return invert;
        }

        if (ShowPaint is { } showPaint)
        {
            yield return showPaint;
        }
    }

    private IPixelShader? Compile(in PixelShaderSource source, PixelShaderUniform[] uniforms)
    {
        var shader = _renderer.CompilePixelShader(source, uniforms);
        if (shader is not null)
        {
            _shaders.Add(shader);
        }

        return shader;
    }
}
