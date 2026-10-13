namespace Basin;

public sealed class PresentedSet
{
    private readonly List<Surface> _surfaces = [];

    internal PresentedSet(IOutput output) => Output = output;

    public IOutput Output { get; }

    public bool Recorded { get; private set; }

    public bool Retired { get; private set; }

    public long Generation { get; private set; }

    public int Count => _surfaces.Count;

    public Surface this[int index] => _surfaces[index];

    public void Begin()
    {
        _surfaces.Clear();
        Generation++;
        Recorded = true;
    }

    public void Add(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (Retired || surface.IsDestroyed || ReferenceEquals(surface.PresentedStampSet, this) && surface.PresentedStamp == Generation)
        {
            return;
        }

        var main = surface.PresentedMain;
        if (main is null || main.Retired || ReferenceEquals(main, this) || main.Generation > surface.PresentedMainStamp)
        {
            surface.PresentedMain = this;
            surface.PresentedMainStamp = Generation;
        }

        surface.PresentedStampSet = this;
        surface.PresentedStamp = Generation;
        _surfaces.Add(surface);
    }

    public bool IsMain(Surface surface) => ReferenceEquals(surface.PresentedMain, this);

    internal void Retire()
    {
        Retired = true;
        _surfaces.Clear();
    }
}
