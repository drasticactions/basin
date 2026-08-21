namespace Basin.Capabilities;

public readonly record struct CaptureSource
{
    private CaptureSource(CaptureSourceKind kind, IOutput? output, ulong toplevelId)
    {
        Kind = kind;
        OutputTarget = output;
        ToplevelId = toplevelId;
    }

    public CaptureSourceKind Kind { get; }

    public IOutput? OutputTarget { get; }

    public ulong ToplevelId { get; }

    public static CaptureSource Output(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new CaptureSource(CaptureSourceKind.Output, output, 0);
    }

    public static CaptureSource Toplevel(ulong toplevelId) =>
        new(CaptureSourceKind.Toplevel, null, toplevelId);

    public static CaptureSource Cursor(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new CaptureSource(CaptureSourceKind.Cursor, output, 0);
    }
}
