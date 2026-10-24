using Basin;
using Basin.Scene3D;

namespace DungeonWm;

internal sealed class DungeonWmSnapshot
{
    public DungeonWmSnapshot(string appId)
    {
        AppId = appId;
        World = new WorldWindow { Tag = this, Pickable = false };
    }

    public string AppId { get; }

    public WorldWindow World { get; }

    public MapRegion? Room { get; set; }

    public BufferBase? Canvas { get; set; }

    public ITexture? CanvasTexture { get; set; }

    public bool FromWall { get; set; }

    public float Elapsed { get; set; }

    public float Fading { get; set; } = -1;

    public float FadeFrom { get; set; } = 1;
}
