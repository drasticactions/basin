namespace Basin.Scene;

public interface IPostStage
{
    void Render(IRenderPass pass, ITexture frame, in PostContext context);

    bool TryMapBox(in Box scene, in PostContext context, out Box output)
    {
        output = scene;
        return false;
    }
}
