namespace Basin;

public sealed record RenderStack(IRenderer Renderer, IAllocator? DeviceAllocator)
{
    public bool NeedsMappableTarget => DeviceAllocator is null;
}
