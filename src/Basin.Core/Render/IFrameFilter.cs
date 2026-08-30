namespace Basin;

public interface IFrameFilter
{
    bool IsSupported { get; }

    bool NeedsFullFrame => true;

    bool NeedsContinuousRepaint => false;
}
