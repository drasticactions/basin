namespace DeskbarWm;

internal sealed class SatTab(bool vertical, int position)
{
    public bool Vertical { get; } = vertical;

    public int Position { get; set; } = position;
}
