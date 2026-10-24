namespace Basin.Rashader;

/// <summary>
/// Reports whether the librashader native library can load on this host.
/// </summary>
public static class RashaderLibrary
{
    /// <summary>
    /// Loads librashader on the first call and checks its entry points and ABI version. Later calls
    /// return the cached result. A failed load does not throw.
    /// </summary>
    /// <param name="whyNot">The reason the library cannot load, or <see langword="null"/> when it loads.</param>
    /// <returns><see langword="true"/> when librashader loaded with a compatible ABI.</returns>
    public static bool IsAvailable(out string? whyNot) => RashaderNative.TryLoad(out whyNot);
}
