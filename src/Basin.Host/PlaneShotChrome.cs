using Basin.Scene;

namespace Basin.Host;

public readonly record struct PlaneShotChrome(string Name, IBuffer? Buffer, SceneBuffer? Node = null, string? Detail = null);
