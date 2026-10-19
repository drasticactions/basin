using Basin.Capabilities;

namespace Basin.UI.ImGui;

internal unsafe interface IImGuiBackend : IDisposable
{
    string Name { get; }

    UITargetKind Produces { get; }

    bool HasVertexOffset { get; }

    int FramesInFlight { get; }

    IImGuiTarget? CreateTarget();

    int CreateTexture(int width, int height);

    void UpdateTexture(int texture, int x, int y, int width, int height, byte* pixels, int pitch);

    void DestroyTexture(int texture);
}
