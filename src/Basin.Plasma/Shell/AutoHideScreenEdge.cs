using Basin.Plasma.Protocol;
using Basin.Shell.Xdg;

namespace Basin.Plasma;

public sealed class AutoHideScreenEdge
{
    private readonly ScreenEdgeManager _owner;
    private readonly KdeAutoHideScreenEdgeV1Resource _resource;
    private IDisposable? _armToken;
    private bool _released;

    internal AutoHideScreenEdge(
        ScreenEdgeManager owner,
        KdeAutoHideScreenEdgeV1Resource resource,
        LayerSurface layer,
        LayerAnchor border)
    {
        _owner = owner;
        _resource = resource;
        Layer = layer;
        Border = border;
        resource.Activate += (_, _) => Activate();
        resource.Deactivate += (_, _) => Deactivate();
        resource.Destroyed += (_, _) => Release();
        layer.Mapped += OnLayerMapped;
        layer.Destroyed += OnLayerDestroyed;
    }

    public LayerSurface Layer { get; }

    public LayerAnchor Border { get; }

    public bool IsHidden { get; private set; }

    public bool IsArmed => _armToken is not null;

    public event Action? Removed;

    internal void Release()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        Layer.Mapped -= OnLayerMapped;
        Layer.Destroyed -= OnLayerDestroyed;
        Deactivate();
        Removed?.Invoke();
    }

    private void Activate()
    {
        _armToken?.Dispose();
        _armToken = null;
        Hide();
        if (_owner.OutputFor(Layer) is { } output)
        {
            _armToken = _owner.ArmEdge(Border, output, OnTriggered);
        }
    }

    private void Deactivate()
    {
        _armToken?.Dispose();
        _armToken = null;
        Reveal();
    }

    private void OnTriggered()
    {
        _armToken = null;
        Reveal();
    }

    private void OnLayerMapped()
    {
        if (IsHidden && _owner.NodeFor(Layer.Surface) is { IsDestroyed: false } node)
        {
            node.Enabled = false;
            _owner.NotifyChanged();
        }
    }

    private void OnLayerDestroyed()
    {
        _armToken?.Dispose();
        _armToken = null;
        IsHidden = false;
    }

    private void Hide()
    {
        if (IsHidden)
        {
            return;
        }

        IsHidden = true;
        if (_owner.NodeFor(Layer.Surface) is { IsDestroyed: false } node)
        {
            node.Enabled = false;
        }

        _owner.NotifyChanged();
    }

    private void Reveal()
    {
        if (!IsHidden)
        {
            return;
        }

        IsHidden = false;
        if (_owner.NodeFor(Layer.Surface) is { IsDestroyed: false } node)
        {
            node.Enabled = true;
        }

        _owner.NotifyChanged();
    }
}
