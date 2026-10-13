namespace Basin.Host;

public interface IHandoffOutro
{
    TimeSpan Duration { get; }

    bool Ready => true;

    void Begin(OutputView view);

    void Step(OutputView view, double progress);

    void End(OutputView view);
}
