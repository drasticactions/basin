namespace Basin.Capabilities;

public interface IUIScreenSource
{
    int Count { get; }

    bool TryGet(int index, out UIScreenInfo info);

    event Action? Changed;
}
