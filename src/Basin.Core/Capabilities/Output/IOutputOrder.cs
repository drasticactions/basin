namespace Basin.Capabilities;

public interface IOutputOrder
{
    int Enumerate(Span<IOutput> outputs);

    event Action? Changed;
}
