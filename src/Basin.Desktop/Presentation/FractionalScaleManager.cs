using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class FractionalScaleManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly OutputLayout? _layout;
    private readonly Dictionary<Surface, WpFractionalScaleV1Resource> _scales = [];
    private readonly Dictionary<Surface, uint> _preferred = [];
    private readonly List<Surface> _sweep = [];
    private bool _consumerAnnounces;

    public double DefaultScale { get; set; }

    public FractionalScaleManager(WlServerDisplay display, CompositorGlobal compositor, OutputLayout? layout = null)
    {
        _compositor = compositor;
        _layout = layout;
        _global = display.CreateGlobal(WpFractionalScaleManagerV1.Interface, Version, OnBind);
        if (_layout is { } outputs)
        {
            outputs.Changed += FollowLayout;
        }
    }

    public void Dispose()
    {
        if (_layout is { } outputs)
        {
            outputs.Changed -= FollowLayout;
        }

        _global.Dispose();
    }

    private void FollowLayout()
    {
        if (_consumerAnnounces || _scales.Count == 0)
        {
            return;
        }

        _sweep.Clear();
        _sweep.AddRange(_scales.Keys);
        foreach (var surface in _sweep)
        {
            FollowOutputs(surface);
        }

        _sweep.Clear();
    }

    public void AnnounceScale(Surface surface, double scale)
    {
        _consumerAnnounces = true;
        SetPreferredScale(surface, scale);
        surface.SetPreferredBufferScale(OutputScaling.CeilScale(scale));
    }

    public void SetPreferredScale(Surface surface, double scale)
    {
        _consumerAnnounces = true;
        Announce(surface, scale);
    }

    private void FollowOutputs(Surface surface)
    {
        if (_consumerAnnounces)
        {
            return;
        }

        var scale = surface.EnteredOutputScale;
        if (scale <= 0)
        {
            return;
        }

        Announce(surface, scale);
        surface.SetPreferredBufferScale(OutputScaling.CeilScale(scale));
    }

    private uint InitialScale(Surface surface)
    {
        if (_preferred.TryGetValue(surface, out var known))
        {
            return known;
        }

        var scale = surface.EnteredOutputScale;
        if (scale <= 0)
        {
            scale = DefaultScale;
        }

        if (scale <= 0 && _layout is { } layout)
        {
            foreach (var (output, _) in layout.Outputs)
            {
                if (output.Scale > 0)
                {
                    scale = output.Scale;
                    break;
                }
            }
        }

        var value = (uint)Math.Round((scale > 0 ? scale : 1.0) * 120);
        _preferred[surface] = value;
        return value;
    }

    private void Announce(Surface surface, double scale)
    {
        var value = (uint)Math.Round(scale * 120);
        if (_preferred.TryGetValue(surface, out var existing) && existing == value)
        {
            return;
        }

        _preferred[surface] = value;
        if (_scales.TryGetValue(surface, out var resource) && !resource.IsDestroyed)
        {
            resource.SendPreferredScale(value);
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpFractionalScaleManagerV1Resource(client, version, id);
        manager.GetFractionalScale += (_, e) =>
        {
            var surface = _compositor.ResolveSurface(e.Surface);
            var resource = new WpFractionalScaleV1Resource(client, manager.Version, e.Id);
            if (surface is null)
            {
                return;
            }

            FollowOutputs(surface);
            _scales[surface] = resource;
            void OnOutputs() => FollowOutputs(surface);
            surface.OutputPresenceChanged += OnOutputs;
            resource.Destroyed += (_, _) =>
            {
                surface.OutputPresenceChanged -= OnOutputs;
                _scales.Remove(surface);
            };
            surface.Destroyed += () =>
            {
                surface.OutputPresenceChanged -= OnOutputs;
                _scales.Remove(surface);
                _preferred.Remove(surface);
            };
            resource.SendPreferredScale(InitialScale(surface));
        };
    }
}
