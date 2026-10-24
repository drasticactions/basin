using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_fractional_scale_manager_v1</c> global. It follows the scale of the outputs that a surface enters until the consumer announces scales itself.
/// </summary>
public sealed class FractionalScaleManager : IDisposable
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly OutputLayout? _layout;
    private readonly Dictionary<Surface, WpFractionalScaleV1Resource> _scales = [];
    private readonly Dictionary<Surface, uint> _preferred = [];
    private readonly List<Surface> _sweep = [];
    private bool _consumerAnnounces;

    /// <summary>
    /// Gets or sets the scale for a surface that has entered no output. With 0 or less, the first output with a scale sets it, and then 1.
    /// </summary>
    public double DefaultScale { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FractionalScaleManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global whose surfaces get a scale.</param>
    /// <param name="layout">The layout whose changes send new scales, or <see langword="null"/>.</param>
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

    /// <summary>
    /// Stops following the layout and removes the global.
    /// </summary>
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

    /// <summary>
    /// Sends a preferred fractional scale and sets the preferred integer buffer scale to its ceiling. After this call, the manager stops following outputs.
    /// </summary>
    /// <param name="surface">The surface to tell.</param>
    /// <param name="scale">The scale, for example 1.5.</param>
    public void AnnounceScale(Surface surface, double scale)
    {
        _consumerAnnounces = true;
        SetPreferredScale(surface, scale);
        surface.SetPreferredBufferScale(OutputScaling.CeilScale(scale));
    }

    /// <summary>
    /// Sends a preferred fractional scale only. After this call, the manager stops following outputs.
    /// </summary>
    /// <param name="surface">The surface to tell.</param>
    /// <param name="scale">The scale, for example 1.5. It goes on the wire in units of 1/120.</param>
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
