namespace Basin;

public interface IAllocator : IDisposable
{
    DrmFormatSet Formats { get; }

    IBuffer? Allocate(int width, int height, DrmFormat format, ReadOnlySpan<ulong> modifiers, BufferUse use);
}
