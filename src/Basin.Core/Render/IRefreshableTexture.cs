using Pixman;

namespace Basin;

public interface IRefreshableTexture
{
    void MarkDirty();

    void MarkDirty(in Box damage) => MarkDirty();

    bool TryAdopt(IBuffer source, in Box damage) => false;
}
