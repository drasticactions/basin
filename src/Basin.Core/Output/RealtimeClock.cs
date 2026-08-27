namespace Basin;

public static class RealtimeClock
{
    public static long Nanos => (DateTime.UtcNow - DateTime.UnixEpoch).Ticks * 100;
}
