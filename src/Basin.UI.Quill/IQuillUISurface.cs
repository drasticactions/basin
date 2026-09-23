using Basin.Capabilities;
using Prowl.Quill;

namespace Basin.UI.Quill;

public interface IQuillUISurface : IUISurface
{
    Canvas BeginDraw();

    void EndDraw();
}
