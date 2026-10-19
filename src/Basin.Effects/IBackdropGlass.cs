using Basin.Capabilities;

namespace Basin.Effects;

public interface IBackdropGlass : IShapedBackdropEffect, IDisposable
{
    GlassOptions Options { get; set; }

    int ExpandSize { get; }
}
