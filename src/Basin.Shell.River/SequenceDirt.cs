using Basin.Shell.River.Protocol;

namespace Basin.Shell.River;

[Flags]
internal enum SequenceDirt
{
    None = 0,

    Manage = 1,

    ManageLazy = 2,

    Render = 4,
}
