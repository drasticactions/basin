using Pixman;

namespace Basin.Capabilities;

public readonly record struct CaptureSource
{
    private CaptureSource(CaptureSourceKind kind, IOutput? output, ulong toplevelId, bool overlayCursor,
        Box layoutBox = default, double scale = 0, bool clientOnly = false)
    {
        Kind = kind;
        OutputTarget = output;
        ToplevelId = toplevelId;
        OverlayCursor = overlayCursor;
        LayoutBox = layoutBox;
        Scale = scale;
        ClientOnly = clientOnly;
    }

    public CaptureSourceKind Kind { get; }

    public IOutput? OutputTarget { get; }

    public ulong ToplevelId { get; }

    public bool OverlayCursor { get; }

    public Box LayoutBox { get; }

    public double Scale { get; }

    public bool ClientOnly { get; }

    public static CaptureSource Output(IOutput output, bool overlayCursor = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new CaptureSource(CaptureSourceKind.Output, output, 0, overlayCursor);
    }

    public static CaptureSource Toplevel(ulong toplevelId, bool clientOnly = false, bool overlayCursor = false) =>
        new(CaptureSourceKind.Toplevel, null, toplevelId, overlayCursor, clientOnly: clientOnly);

    public static CaptureSource Cursor(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new CaptureSource(CaptureSourceKind.Cursor, output, 0, false);
    }

    public static CaptureSource Region(in Box layoutBox, double scale = 0, bool overlayCursor = false) =>
        new(CaptureSourceKind.Region, null, 0, overlayCursor, layoutBox, scale);
}
