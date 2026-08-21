namespace Basin;

public interface IPipeToClient
{
    bool CanWrite { get; }

    void Write(ReadOnlySpan<byte> bytes);

    void CloseWrite();
}
