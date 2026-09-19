namespace Basin.Seat;

public interface IKeySink
{
    void NotifyKey(uint timeMs, uint key, bool pressed);
}
