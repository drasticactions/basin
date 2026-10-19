using Basin.Capabilities;
using Hexa.NET.ImGui;
using Pixman;

namespace Basin.UI.ImGui;

internal unsafe interface IImGuiTarget : IDisposable
{
    bool Configure(int width, int height);

    void Render(ImDrawData* data);

    bool TryAcquire(PixmanRegion32? damage, out UIFrame frame);
}
