namespace Basin.Capabilities;

public interface IActiveKeymap
{
    (int Fd, uint Size)? KeymapBuffer { get; }

    event Action? KeymapChanged;
}
