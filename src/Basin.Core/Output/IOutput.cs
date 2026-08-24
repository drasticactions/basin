using Basin.Capabilities;
using Pixman;

namespace Basin;

public interface IOutput
{
    string Name { get; }

    string Description { get; }

    string Make { get; }

    string Model { get; }

    string Serial { get; }

    (int Width, int Height) PhysicalSize { get; }

    OutputMode CurrentMode { get; }

    bool Enabled { get; }

    double Scale { get; }

    OutputTransform Transform { get; }

    bool AdaptiveSync { get; }

    bool SupportsInFence => false;

    OutputConfigurationFeatures Features => OutputConfigurationFeatures.None;

    OutputColorimetry? Colorimetry => null;

    ReadOnlyMemory<byte> EdidBytes => default;

    bool TestCommit(OutputState state);

    bool Commit(OutputState state);

    event Action? Frame;

    event Action<OutputStateFields>? Committed;

    event Action? Destroyed;
}
