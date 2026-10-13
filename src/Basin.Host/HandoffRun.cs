using Basin.Scene;

namespace Basin.Host;

internal sealed class HandoffRun : IDisposable
{
    private readonly BufferBase? _copy;
    private readonly IDisposable? _owner;
    private readonly SceneTree? _ownedLayer;

    public HandoffRun(
        HandoffFrame frame,
        IHandoffTransition transition,
        SceneTree layer,
        SceneTree? ownedLayer,
        Box output,
        IBuffer shown,
        int sourceWidth,
        int sourceHeight,
        BufferBase? copy,
        IDisposable? owner,
        long created)
    {
        Frame = frame;
        Transition = transition;
        _copy = copy;
        _owner = owner;
        _ownedLayer = ownedLayer;
        Created = created;
        Tree = new SceneTree(layer);
        Tree.SetPosition(output.X, output.Y);
        Node = new SceneBuffer(Tree) { IsOpaque = true, InputEnabled = false };
        Node.SetBuffer(shown);
        Node.SourceBox = new FBox(0, 0, sourceWidth, sourceHeight);
        Transition.Begin(Frame);
        Apply();
    }

    public HandoffFrame Frame { get; }

    public IHandoffTransition Transition { get; }

    public SceneTree Tree { get; }

    public SceneBuffer Node { get; }

    public long Created { get; }

    public long Started { get; set; } = -1;

    public bool Ended { get; private set; }

    public void Apply()
    {
        Tree.Alpha = Frame.Opacity;
        Node.SetPosition(Frame.Destination.X, Frame.Destination.Y);
        Node.DestinationWidth = Frame.Destination.Width;
        Node.DestinationHeight = Frame.Destination.Height;
        Node.ClipBox = Frame.Clip;
        Tree.Enabled = Frame.Opacity > 0f;
    }

    public void End()
    {
        if (Ended)
        {
            return;
        }

        Ended = true;
        Transition.End(Frame);
        Apply();
    }

    public void Dispose()
    {
        Tree.Destroy();
        _ownedLayer?.Destroy();
        _copy?.Destroy();
        _owner?.Dispose();
    }
}
