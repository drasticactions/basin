namespace Basin.WindowManager;

[Flags]
public enum WmFdReadiness : uint
{
    None = 0,

    Readable = 1,

    Writable = 2,

    Hangup = 4,

    Error = 8,
}
