using Basin.Capabilities;
using Basin.UI.ImGui;
using Hexa.NET.ImGui;
using Pixman;

namespace Basin.Tests;

internal sealed unsafe class NullImGuiBackend : IImGuiBackend
{
    private readonly List<(int Width, int Height)?> _textures = [];

    public string Name => "null";

    public UITargetKind Produces => UITargetKind.Memory;

    public bool HasVertexOffset { get; init; } = true;

    public int FramesInFlight => 2;

    public int Renders { get; private set; }

    public int Uploads { get; private set; }

    public int LiveTextures => _textures.Count(texture => texture is not null);

    public bool Disposed { get; private set; }

    public IImGuiTarget? CreateTarget() => new Target(this);

    public int CreateTexture(int width, int height)
    {
        _textures.Add((width, height));
        return _textures.Count - 1;
    }

    public void UpdateTexture(int texture, int x, int y, int width, int height, byte* pixels, int pitch)
    {
        if ((uint)texture >= (uint)_textures.Count || _textures[texture] is not { } size)
        {
            throw new InvalidOperationException($"texture {texture} is not live");
        }

        if (x < 0 || y < 0 || x + width > size.Width || y + height > size.Height || pitch < width * 4)
        {
            throw new InvalidOperationException($"update {x},{y} {width}x{height} leaves texture {texture}");
        }

        Uploads++;
    }

    public void DestroyTexture(int texture) => _textures[texture] = null;

    public void Dispose()
    {
        Disposed = true;
        _textures.Clear();
    }

    private sealed class Target(NullImGuiBackend backend) : IImGuiTarget
    {
        public bool Configure(int width, int height) => width > 0 && height > 0;

        public void Render(ImDrawData* data) => backend.Renders++;

        public bool TryAcquire(PixmanRegion32? damage, out UIFrame frame)
        {
            frame = default;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
