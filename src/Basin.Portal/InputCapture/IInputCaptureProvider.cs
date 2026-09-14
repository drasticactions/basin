using Basin.Eis;

namespace Basin.Portal;

public interface IInputCaptureProvider
{
    uint ZoneSet { get; }

    IInputCaptureHandle? Open(IInputCaptureFront front, string handle);
}
