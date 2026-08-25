using Basin;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Effects;
using Basin.Plasma;
using Basin.Scene;
using Pixman;

namespace PlasmaHost;

internal sealed class PlasmaHostBackdrops : IDisposable
{
    private readonly IBackdropBlur? _blur;
    private readonly List<Binding> _bindings = [];
    private readonly PixmanRegion32 _whole = new();
    private BlurManager? _kdeBlur;
    private ContrastManager? _contrast;
    private BackgroundEffectManager? _ext;

    public PlasmaHostBackdrops(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _blur = renderer switch
        {
            Basin.Render.Vulkan.VulkanRenderer vulkan => new VulkanBackdropBlur(vulkan.Device),
            Basin.Render.Gl.GlRenderer gl => new GlBackdropBlur(gl.Device),
            _ => null,
        };

        _whole.Reset(new PixmanBox32(0, 0, 1 << 15, 1 << 15));
    }

    public IBackgroundEffects? Capability => _blur;

    public int AttachedCount
    {
        get
        {
            var count = 0;
            foreach (var binding in _bindings)
            {
                if (binding.IsAttached)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int BindingCount => _bindings.Count;

    public BlurOptions Options
    {
        get => _blur?.Options ?? new BlurOptions();
        set
        {
            if (_blur is not null)
            {
                _blur.Options = value;
            }
        }
    }

    public BlurCorners Corners
    {
        get => _blur?.Corners ?? default;
        set
        {
            if (_blur is not null)
            {
                _blur.Corners = value;
            }
        }
    }

    public void Bind(BlurManager kdeBlur, ContrastManager contrast, BackgroundEffectManager ext)
    {
        _kdeBlur = kdeBlur;
        _contrast = contrast;
        _ext = ext;
    }

    public void Attach(SceneSurface scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_blur is null)
        {
            return;
        }

        var binding = new Binding(this, scene);
        _bindings.Add(binding);
        scene.Destroyed += () => _bindings.Remove(binding);
    }

    public void Dispose()
    {
        foreach (var binding in _bindings.ToArray())
        {
            binding.Dispose();
        }

        _bindings.Clear();
        _whole.Dispose();
        _blur?.Dispose();
    }

    private sealed class Binding : IDisposable
    {
        private readonly PlasmaHostBackdrops _owner;
        private readonly SceneSurface _scene;
        private readonly Action _refresh;
        private bool _attached;

        public bool IsAttached => _attached;
        private bool _disposed;

        public Binding(PlasmaHostBackdrops owner, SceneSurface scene)
        {
            _owner = owner;
            _scene = scene;
            _refresh = Refresh;
            scene.Surface.Committed += _refresh;
            scene.Destroyed += Dispose;
            Refresh();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _scene.Surface.Committed -= _refresh;
            _scene.Destroyed -= Dispose;
            if (_attached && !_scene.Content.IsDestroyed)
            {
                _scene.Content.SetBackdropEffect(null, null);
            }

            _owner._blur?.ForgetSurface(_scene);
        }

        private void Refresh()
        {
            if (_disposed || _owner._blur is not { } effect || _scene.Content.IsDestroyed)
            {
                return;
            }

            var surface = _scene.Surface;
            var extRegion = _owner._ext?.BlurRegionOf(surface);
            var kdeBlur = _owner._kdeBlur?.BlurOf(surface);
            var contrast = _owner._contrast?.ContrastOf(surface);
            var parameters = new ContrastParameters(1.0, 1.0, 1.0);
            var hasContrast = _owner._contrast is { } manager && manager.TryGetContrast(surface, out parameters);

            var blurs = extRegion is not null || kdeBlur is not null;
            if (!blurs && !hasContrast)
            {
                if (_attached)
                {
                    _scene.Content.SetBackdropEffect(null, null);
                    effect.ForgetSurface(_scene);
                    _attached = false;
                }

                return;
            }

            var region = extRegion
                ?? (kdeBlur is { WholeSurface: false } ? kdeBlur.Region : null)
                ?? (blurs ? _owner._whole : null)
                ?? (contrast is { WholeSurface: false } ? contrast.Region : _owner._whole);

            effect.SetSurface(_scene, new BlurSurfaceOptions
            {
                Blur = blurs,
                Contrast = hasContrast,
                ContrastParameters = parameters,
            });
            _scene.Content.SetBackdropEffect(effect, region, _scene);
            _attached = true;
        }
    }
}
