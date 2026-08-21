namespace Basin.Backend.Headless;

public sealed class HeadlessPointer
{
    internal HeadlessPointer(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public event Action<uint, double, double>? Motion;

    public event Action<uint, double, double>? RelativeMotion;

    public event Action<uint, uint, bool>? Button;

    public event Action<uint, PointerAxis>? Axis;

    public event Action? Frame;

    public void InjectMotion(uint timeMs, double x, double y) => Motion?.Invoke(timeMs, x, y);

    public void InjectRelativeMotion(uint timeMs, double dx, double dy) => RelativeMotion?.Invoke(timeMs, dx, dy);

    public void InjectButton(uint timeMs, uint button, bool pressed) => Button?.Invoke(timeMs, button, pressed);

    public void InjectAxis(uint timeMs, in PointerAxis axis) => Axis?.Invoke(timeMs, axis);

    public void InjectFrame() => Frame?.Invoke();
}
