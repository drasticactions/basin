namespace Basin.Capabilities;

public interface IOutputLooks
{
    string? LookOf(IOutput output);

    bool SetLook(IOutput output, string? path, out string? error);
}
