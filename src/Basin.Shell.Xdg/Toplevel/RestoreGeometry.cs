namespace Basin.Shell.Xdg;

public readonly record struct RestoreGeometry
{
    public static RestoreGeometry None => default;

    public bool HasValue { get; private init; }

    public Box Frame { get; private init; }

    public RestoreGeometry Saving(in Box frame) =>
        HasValue || frame.IsEmpty ? this : new RestoreGeometry { HasValue = true, Frame = frame };

    public bool TryGet(out Box frame)
    {
        frame = Frame;
        return HasValue;
    }
}
