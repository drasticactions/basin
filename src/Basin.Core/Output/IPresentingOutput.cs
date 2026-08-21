namespace Basin;

public interface IPresentingOutput
{
    event Action<ulong, uint, ulong>? PresentedOnScreen;

    event Action? PresentationDiscarded
    {
        add
        {
        }

        remove
        {
        }
    }
}
