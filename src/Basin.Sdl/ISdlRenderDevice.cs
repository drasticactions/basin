using SDL3;
using SkiaSharp;

namespace Basin.Sdl;

public interface ISdlRenderDevice : IDisposable
{
    string Name { get; }

    SDL.WindowFlags WindowFlags { get; }

    GRContext? Context { get; }

    nint EglDisplay { get; }

    IRenderer? DmabufRenderer => null;

    string? RenderNode => null;

    bool TryRecover() => false;

    ISdlRenderLease CreateLease(SdlWindow window);
}
