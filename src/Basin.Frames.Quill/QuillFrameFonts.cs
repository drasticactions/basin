using System.Reflection;
using Prowl.Scribe;

namespace Basin.Frames.Quill;

/// <summary>
/// The font that the Quill frames embed for titles and menu labels.
/// </summary>
public static class QuillFrameFonts
{
    /// <summary>
    /// The manifest resource name of the embedded Selawik font.
    /// </summary>
    public const string ResourceName = "Selawik-Regular.ttf";

    private static FontFile? _bundled;

    /// <summary>
    /// Loads the embedded font on the first call and gives the same instance after that. Call it on the compositor thread.
    /// </summary>
    /// <returns>The embedded font.</returns>
    /// <exception cref="InvalidOperationException">The assembly does not contain <see cref="ResourceName"/>.</exception>
    public static FontFile Bundled()
    {
        if (_bundled is not null)
        {
            return _bundled;
        }

        using var stream = typeof(QuillFrameFonts).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Basin.Frames.Quill is missing {ResourceName}.");
        _bundled = new FontFile(stream);
        return _bundled;
    }
}
