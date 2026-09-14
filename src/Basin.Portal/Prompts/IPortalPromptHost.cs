using Basin.Capabilities;

namespace Basin.Portal;

public interface IPortalPromptHost
{
    IOutput? OutputFor(string parentWindow);

    void Show(IUISurface surface, IOutput output);

    void Hide(IUISurface surface);
}
