namespace Basin.Capabilities;

public interface IFrameClock : IFrameSink
{
    void Add(IFrameSink sink);

    void Remove(IFrameSink sink);
}
