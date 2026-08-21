namespace Basin.Capabilities;

[Flags]
public enum UITargetKind
{
    Memory = 1,

    Dmabuf = 2,

    GlTexture = 4,
}
