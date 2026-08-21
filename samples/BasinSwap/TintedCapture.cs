using System.Diagnostics;
using Basin;
using Basin.Backend.Headless;
using Basin.Capabilities;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Render.Pixman;
using Basin.Scene;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Samples.Swap;

internal sealed class TintedCapture : IScreenCapture, ICaptureDamageObserver
{
    private readonly SceneScreenCapture _inner;

    public TintedCapture(Scene.Scene scene, OutputLayout layout, IRenderer renderer)
    {
        _inner = new SceneScreenCapture(scene, layout) { Renderer = renderer };
        _inner.AddDamageObserver(this);
    }

    private readonly CaptureDamageObservers _damageObservers = new();

    public void AddDamageObserver(ICaptureDamageObserver observer) => _damageObservers.Add(observer);

    public void RemoveDamageObserver(ICaptureDamageObserver observer) => _damageObservers.Remove(observer);

    public void OnSourceDamaged(IOutput output, Box damage) => _damageObservers.Damaged(output, damage);

    public int Captures { get; private set; }

    public void NotifyDamaged(IOutput output, Box damage) => _inner.NotifyDamaged(output, damage);

    public bool Supports(in CaptureSource source) => _inner.Supports(source);

    public bool TryDescribe(in CaptureSource source, out CaptureFormat format) =>
        _inner.TryDescribe(source, out format);

    public bool TryCursorState(IOutput output, out CaptureCursorState cursor) =>
        _inner.TryCursorState(output, out cursor);

    public unsafe bool Capture(in CaptureSource source, in Box region, IBuffer target)
    {
        if (!_inner.Capture(source, region, target))
        {
            return false;
        }

        Captures++;
        if (!target.BeginDataAccess(BufferDataAccess.Read | BufferDataAccess.Write, out var access))
        {
            return false;
        }

        try
        {
            for (var y = 0; y < target.Height; y++)
            {
                var row = (uint*)(access.Data + (y * access.Stride));
                for (var x = 0; x < target.Width; x++)
                {
                    row[x] |= 0x00004000u;
                }
            }
        }
        finally
        {
            target.EndDataAccess();
        }

        return true;
    }
}
