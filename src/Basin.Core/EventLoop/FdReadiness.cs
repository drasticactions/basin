namespace Basin;

[Flags]
public enum FdReadiness : uint
{
    None = 0,
    Readable = 1,
    Writable = 2,
    Hangup = 4,
    Error = 8,
}
