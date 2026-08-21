using Basin.Capabilities;
using Basin.Scene;
using Basin.Testing;

namespace Basin.Tests;

public sealed class SceneScreenCaptureConformance : ScreenCaptureConformance, IDisposable
{
    private readonly CompositorTestHost _host = new();

    protected override IOutput Output => _host.Output;

    public void Dispose() => _host.Dispose();

    protected override IScreenCapture Create() =>
        new SceneScreenCapture(_host.Scene, _host.Layout) { Renderer = _host.Renderer };

    protected override IBuffer CreateTarget(in CaptureFormat format) =>
        new MemoryBuffer(format.Width, format.Height, format.Format);

    protected override void DestroyTarget(IBuffer target) => (target as MemoryBuffer)?.Destroy();
}

public sealed class SeatSelectionStoreConformance : SelectionStoreConformance, IDisposable
{
    private readonly CompositorTestHost _host = new();

    public void Dispose() => _host.Dispose();

    protected override ISelectionStore Create() => new Basin.Seat.SeatSelectionStore(_host.Seat);
}
