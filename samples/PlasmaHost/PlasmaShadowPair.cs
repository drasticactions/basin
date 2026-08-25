using Basin;
using Basin.Effects;
using Basin.Scene;

namespace PlasmaHost;

internal sealed class PlasmaShadowPair : IDisposable
{
    private readonly SceneTransform _activeTree;
    private readonly SceneTransform _inactiveTree;
    private readonly DropShadowEffect _active;
    private readonly DropShadowEffect _inactive;
    private EffectTimeline _fade;
    private bool _wantActive = true;
    private bool _fading;
    private bool _disposed;

    public PlasmaShadowPair(SceneTree parent)
    {
        _inactiveTree = new SceneTransform(parent);
        _activeTree = new SceneTransform(parent);
        _inactive = new DropShadowEffect(_inactiveTree);
        _active = new DropShadowEffect(_activeTree);
        _activeTree.Alpha = 1f;
        _inactiveTree.Alpha = 0f;
        _inactiveTree.LowerToBottom();
        _activeTree.LowerToBottom();
    }

    public bool IsFading => _fading;

    public bool Visible
    {
        set
        {
            _active.Visible = value;
            _inactive.Visible = value;
        }
    }

    public Box Geometry => _active.Geometry;

    public void SetTextures(DropShadowTexture? active, DropShadowTexture? inactive)
    {
        _active.Texture = active;
        _inactive.Texture = inactive;
    }

    public void SetGeometry(in Box outer)
    {
        _active.SetGeometry(outer);
        _inactive.SetGeometry(outer);
    }

    public void SetActive(bool active, long durationNanos)
    {
        if (_wantActive == active)
        {
            return;
        }

        _wantActive = active;
        if (durationNanos <= 0)
        {
            _activeTree.Alpha = active ? 1f : 0f;
            _inactiveTree.Alpha = active ? 0f : 1f;
            return;
        }

        _fading = true;
        _fade.Start(durationNanos);
    }

    public bool Step(in FrameTick tick)
    {
        if (!_fading)
        {
            return false;
        }

        var running = _fade.Running(tick);
        var progress = (float)_fade.Progress(tick);
        var target = _wantActive ? progress : 1f - progress;
        _activeTree.Alpha = target;
        _inactiveTree.Alpha = 1f - target;
        if (!running)
        {
            _fading = false;
        }

        return _fading;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _active.Dispose();
        _inactive.Dispose();
        _activeTree.Destroy();
        _inactiveTree.Destroy();
    }
}
