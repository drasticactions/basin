namespace Basin.Render.Gl;

public interface IGlBackdropEffect : IBackdropEffect
{
    bool Record(in GlBackdropContext context, out GlBackdropResult result);
}
