using System.Reflection;
using Prowl.Scribe;

namespace Basin.Frames.Quill;

public static class QuillFrameFonts
{
    public const string ResourceName = "Selawik-Regular.ttf";

    private static FontFile? _bundled;

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
