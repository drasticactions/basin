using SDL3;

namespace Basin.Sdl;

public interface ISdlEventSink
{
    bool Handle(in SDL.Event e);
}
