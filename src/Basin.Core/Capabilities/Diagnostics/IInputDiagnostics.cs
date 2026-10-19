namespace Basin.Capabilities;

public interface IInputDiagnostics
{
    IDisposable WatchInput(bool motion, Action<InputTraceEvent> sink);
}
