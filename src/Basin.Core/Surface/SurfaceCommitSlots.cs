namespace Basin;

public static class SurfaceCommitSlots
{
    private static int _next;

    public static int Count => _next;

    public static int Reserve() => _next++;
}
