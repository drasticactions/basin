using System.Runtime.InteropServices;

namespace Basin.UI.Quill;

[StructLayout(LayoutKind.Explicit, Size = 248)]
internal unsafe struct QuillVulkanUniforms
{
    [FieldOffset(0)]
    public fixed float Projection[16];

    [FieldOffset(64)]
    public fixed float ScissorTransform[4];

    [FieldOffset(80)]
    public fixed float ScissorTranslation[2];

    [FieldOffset(88)]
    public fixed float ScissorExt[2];

    [FieldOffset(96)]
    public fixed float BrushTransform[4];

    [FieldOffset(112)]
    public fixed float BrushTranslation[2];

    [FieldOffset(120)]
    public int BrushType;

    [FieldOffset(128)]
    public fixed float BrushColor1[4];

    [FieldOffset(144)]
    public fixed float BrushColor2[4];

    [FieldOffset(160)]
    public fixed float BrushParams[4];

    [FieldOffset(176)]
    public fixed float BrushParams2[2];

    [FieldOffset(192)]
    public fixed float TextureTransform[4];

    [FieldOffset(208)]
    public fixed float TextureTranslation[2];

    [FieldOffset(216)]
    public fixed float AtlasTexelSize[2];

    [FieldOffset(224)]
    public float SdfPxRange;

    [FieldOffset(232)]
    public fixed float ViewportSize[2];

    [FieldOffset(240)]
    public float BackdropBlurAmount;

    [FieldOffset(244)]
    public int BackdropFlipY;
}
