namespace Basin.Capabilities;

public interface IFrameSink
{
    void BeginFrame(IOutput output, long predictedVblankNanos);

    void EndFrame(IOutput output, long presentedNanos);
}
