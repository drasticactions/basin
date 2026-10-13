namespace Basin;

public readonly record struct HandoffRetention(string Output, uint FramebufferId, string? Refusal)
{
    public bool Retained => Refusal is null;
}
