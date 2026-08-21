namespace Basin;

public interface IPipeFromClient
{
    void Deliver(ReadOnlySpan<byte> bytes);

    void Complete();
}
