using Basin.Capabilities;

namespace Basin.Tests;

internal sealed class ManualSettle : IFrameSettle
{
    public bool IsSettled { get; set; } = true;

    public long Presented { get; set; }

    public int Listeners => Settled?.GetInvocationList().Length ?? 0;

    public string Unsettled { get; set; } = "";

    public event Action? Settled;

    public string DescribeUnsettled() => IsSettled ? "" : Unsettled;

    public void Settle(long presented)
    {
        IsSettled = true;
        Presented = presented;
        Settled?.Invoke();
    }
}
