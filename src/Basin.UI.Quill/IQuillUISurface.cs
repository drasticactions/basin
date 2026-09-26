using Basin.Capabilities;
using Prowl.Quill;

namespace Basin.UI.Quill;

public interface IQuillUISurface : IUISurface
{
    ICanvasRenderer Renderer { get; }

    FontAtlasSettings Atlas { get; }

    void BeginTarget();

    void EndTarget();

    Canvas BeginDraw();

    void EndDraw();
}
