namespace Basin.Scene;

public sealed partial class Scene
{
    public int AttachLuts(Func<Surface, IColorLut?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        return AttachLuts(Root, resolve);
    }

    private static int AttachLuts(SceneTree tree, Func<Surface, IColorLut?> resolve)
    {
        var attached = 0;
        foreach (var node in tree.Children)
        {
            switch (node)
            {
                case SceneBuffer { InputSurface: { } surface } buffer:
                    buffer.Lut = resolve(surface);
                    attached += buffer.Lut is null ? 0 : 1;
                    break;
                case SceneTree subtree:
                    attached += AttachLuts(subtree, resolve);
                    break;
            }
        }

        return attached;
    }
}
