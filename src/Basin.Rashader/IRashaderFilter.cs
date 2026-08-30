namespace Basin.Rashader;

public interface IRashaderFilter : IFrameFilter, IDisposable
{
    IReadOnlyList<RashaderParameter> Parameters { get; }

    bool TrySetParameter(string name, float value);
}
