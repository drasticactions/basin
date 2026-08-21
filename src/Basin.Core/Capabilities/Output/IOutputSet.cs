namespace Basin.Capabilities;

public interface IOutputSet
{
    IReadOnlyList<IOutput> Outputs { get; }

    event Action? Changed;
}
