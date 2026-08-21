namespace Basin.Capabilities;

public readonly record struct CaptureSource
{
    private CaptureSource(CaptureSourceKind kind, IOutput? output, ulong toplevelId, bool overlayCursor)
    {
        Kind = kind;
        OutputTarget = output;
        ToplevelId = toplevelId;
        OverlayCursor = overlayCursor;
    }

    public CaptureSourceKind Kind { get; }

    public IOutput? OutputTarget { get; }

    public ulong ToplevelId { get; }

    public bool OverlayCursor { get; }

    public static CaptureSource Output(IOutput output, bool overlayCursor = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new CaptureSource(CaptureSourceKind.Output, output, 0, overlayCursor);
    }

    public static CaptureSource Toplevel(ulong toplevelId) =>
        new(CaptureSourceKind.Toplevel, null, toplevelId, false);

    public static CaptureSource Cursor(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new CaptureSource(CaptureSourceKind.Cursor, output, 0, false);
    }
}
