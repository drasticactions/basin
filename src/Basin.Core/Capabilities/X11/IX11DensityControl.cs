namespace Basin.Capabilities;

public interface IX11DensityControl
{
    bool TrySet(ulong toplevelId, X11Density density, Action<X11DensityResult> completed, out string? error);
}
