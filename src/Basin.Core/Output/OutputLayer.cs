namespace Basin;

public sealed class OutputLayer
{
    public IBuffer? Buffer { get; set; }

    public FBox SrcBox { get; set; }

    public Box DstBox { get; set; }

    public float Alpha { get; set; } = 1f;

    public bool Opaque { get; set; }

    public int InFenceFd { get; set; } = -1;

    public bool Accepted { get; set; }
}
