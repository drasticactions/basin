namespace DeskbarWm;

internal readonly record struct DeskbarPlacement(
    BarOrientation Orientation,
    BarSide Side,
    BarEnd End,
    DeskbarState State)
{
    public static DeskbarPlacement Default => new(BarOrientation.Vertical, BarSide.Right, BarEnd.Top, DeskbarState.Expando);

    public DeskbarPlacement Normalize(out string? warning)
    {
        if (Orientation == BarOrientation.Horizontal && State == DeskbarState.Full)
        {
            warning = "[deskbar] there is no horizontal full state, horizontal expando is used";
            return this with { State = DeskbarState.Expando };
        }

        warning = null;
        return this;
    }
}
