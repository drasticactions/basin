using Basin.WindowManager;

namespace DeskbarWm;

internal readonly record struct DecorationHit(FramePart Part, Edges Edges)
{
    public static DecorationHit None => new(FramePart.None, Edges.None);
}
