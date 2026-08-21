using Avalonia.Rendering;

namespace Basin.UI.Avalonia;

internal sealed class BasinRenderTimer : IRenderTimer
{
    public Action<TimeSpan>? Tick { get; set; }

    public bool RunsInBackground => false;

    public void Fire(TimeSpan elapsed) => Tick?.Invoke(elapsed);
}
