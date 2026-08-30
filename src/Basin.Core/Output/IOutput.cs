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

    OutputClass Class => OutputClass.Desktop;

    OutputMode CurrentMode { get; }

    bool Enabled { get; }

    double Scale { get; }

    double AspectRatio => 0;

    OutputTransform Transform { get; }

    bool AdaptiveSync { get; }

    bool SupportsInFence => false;

    bool CanScanout(DrmFormat format, ulong modifier, bool overlay) => true;

    OutputConfigurationFeatures Features => OutputConfigurationFeatures.None;

    OutputColorimetry? Colorimetry => null;

    ReadOnlyMemory<byte> EdidBytes => default;

    void RequestRepaint();

    bool TestCommit(OutputState state);

    bool Commit(OutputState state);

    event Action? Frame;

    event Action? RepaintRequested;

    event Action<OutputStateFields>? Committed;

    event Action? Destroyed;
}
