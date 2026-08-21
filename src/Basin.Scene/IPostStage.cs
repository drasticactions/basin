namespace Basin.Scene;

public interface IPostStage
{
    void Render(IRenderPass pass, ITexture frame, in PostContext context);
}
