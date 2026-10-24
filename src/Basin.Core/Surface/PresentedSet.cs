namespace Basin;

/// <summary>
/// The surfaces that one output drew in its latest frame. Each surface also records its main output, the output that drew it most recently.
/// </summary>
public sealed class PresentedSet
{
    private readonly List<Surface> _surfaces = [];

    internal PresentedSet(IOutput output) => Output = output;

    /// <summary>Gets the output that the set belongs to.</summary>
    public IOutput Output { get; }

    /// <summary>Gets a value indicating whether <see cref="Begin"/> ever ran.</summary>
    public bool Recorded { get; private set; }

    /// <summary>Gets a value indicating whether the output is gone. A retired set is empty and ignores <see cref="Add"/>.</summary>
    public bool Retired { get; private set; }

    /// <summary>Gets the frame number. Each <see cref="Begin"/> increments it.</summary>
    public long Generation { get; private set; }

    /// <summary>Gets the number of surfaces in the current frame.</summary>
    public int Count => _surfaces.Count;

    /// <summary>Gets one surface of the current frame.</summary>
    /// <param name="index">The zero-based index.</param>
    /// <returns>The surface at <paramref name="index"/>.</returns>
    public Surface this[int index] => _surfaces[index];

    /// <summary>Starts a new frame. It empties the set and increments <see cref="Generation"/>.</summary>
    public void Begin()
    {
        _surfaces.Clear();
        Generation++;
        Recorded = true;
    }

    /// <summary>
    /// Records that the output drew a surface in this frame. The set becomes the surface's main output when the old main set is retired, or began a newer frame that has not drawn the surface.
    /// </summary>
    /// <param name="surface">The surface. A destroyed surface or a second add in the same frame is ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
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

    /// <summary>Tests whether this set's output is the surface's main output.</summary>
    /// <param name="surface">The surface.</param>
    /// <returns><see langword="true"/> when this set is the surface's main set.</returns>
    public bool IsMain(Surface surface) => ReferenceEquals(surface.PresentedMain, this);

    internal void Retire()
    {
        Retired = true;
        _surfaces.Clear();
    }
}
