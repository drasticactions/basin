using System.Diagnostics.CodeAnalysis;

namespace Basin.Scene;

public interface IPresentedOutputs
{
    IReadOnlyList<SceneOutput> Outputs { get; }

    event Action<SceneOutput>? Presented;

    bool CapturePresented { get; set; }

    bool TryReadPresented(SceneOutput output, IRenderer renderer, [NotNullWhen(true)] out byte[]? rgba, out int width, out int height);

    void AllowPlanes(bool allowed);

    void RequestRepaint(SceneOutput output);
}
