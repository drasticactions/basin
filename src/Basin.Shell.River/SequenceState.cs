using Basin.Shell.River.Protocol;

namespace Basin.Shell.River;

internal enum SequenceState
{
    Idle,

    Manage,

    AwaitingConfigures,

    Render,
}
