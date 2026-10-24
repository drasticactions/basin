namespace Basin.Tests;

internal sealed class ScriptedOutput(string name) : OutputBase(name)
{
    public bool CommitResult { get; set; } = true;

    public OutputMode SeenMode { get; private set; }

    public bool SeenEnabled { get; private set; }

    public int CommitCoreCalls { get; private set; }

    protected override bool SupportsAdaptiveSync => true;

    protected override bool TestCommitCore(OutputState state) => true;

    protected override bool CommitCore(OutputState state)
    {
        CommitCoreCalls++;
        SeenMode = CurrentMode;
        SeenEnabled = Enabled;
        return CommitResult;
    }
}
