namespace Basin.Capabilities;

public interface IOutputPower
{
    bool IsOn(IOutput output);

    bool SetOn(IOutput output, bool on);

    event Action<IOutput>? PowerChanged;
}
