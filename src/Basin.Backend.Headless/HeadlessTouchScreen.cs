namespace Basin.Backend.Headless;

public sealed class HeadlessTouchScreen
{
    internal HeadlessTouchScreen(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public event Action<uint, int, double, double>? Down;

    public event Action<uint, int>? Up;

    public event Action<uint, int, double, double>? Motion;

    public event Action? Frame;

    public event Action? Cancel;

    public void InjectDown(uint timeMs, int slot, double x, double y) => Down?.Invoke(timeMs, slot, x, y);

    public void InjectUp(uint timeMs, int slot) => Up?.Invoke(timeMs, slot);

    public void InjectMotion(uint timeMs, int slot, double x, double y) => Motion?.Invoke(timeMs, slot, x, y);

    public void InjectFrame() => Frame?.Invoke();

    public void InjectCancel() => Cancel?.Invoke();
}
