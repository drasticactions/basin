namespace Basin.Host;

public interface IHandoffTransition
{
    TimeSpan Duration { get; }

    void Begin(HandoffFrame frame);

    void Step(HandoffFrame frame, double progress);

    void End(HandoffFrame frame);
}
