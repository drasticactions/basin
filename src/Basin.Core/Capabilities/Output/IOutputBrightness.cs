namespace Basin.Capabilities;

public interface IOutputBrightness
{
    bool Supports(IOutput output);

    uint Max(IOutput output);

    bool TryGet(IOutput output, out uint value);

    bool Set(IOutput output, uint value);

    bool UsesDdcCi(IOutput output) => false;

    event Action<IOutput>? BrightnessChanged;
}
