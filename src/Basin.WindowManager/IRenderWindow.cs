using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

public interface IRenderWindow
{
    WmNode Node { get; }

    Size Dimensions { get; }

    void Hide();

    void Show();

    void SetBorders(Edges edges, int width, WmColor color);

    void SetClipBox(Rect box);

    void SetContentClipBox(Rect box);
}
