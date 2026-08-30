namespace Basin.Rashader;

public static class RashaderLibrary
{
    public static bool IsAvailable(out string? whyNot) => RashaderNative.TryLoad(out whyNot);
}
