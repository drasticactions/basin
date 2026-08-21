namespace Basin.Session;

public interface ISessionDevice : IDisposable
{
    int FileDescriptor { get; }

    string Path { get; }
}
