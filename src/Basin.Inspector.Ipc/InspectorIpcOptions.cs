using Basin.Host;

namespace Basin.Inspector.Ipc;

public sealed class InspectorIpcOptions
{
    public required Func<InspectorOverlay?> Overlay { get; init; }

    public required OutputDriver Driver { get; init; }

    public Func<OutputView?>? DefaultOutput { get; init; }

    public Func<string?>? Declined { get; init; }
}
