namespace Basin.Avalonia;

public interface ICaptureTarget
{
    Func<uint, bool, bool>? KeyFilter { get; set; }

    void InjectKey(uint code, bool pressed);

    void CaptureInput(bool captured);
}
