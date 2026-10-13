namespace Basin.Capabilities;

public readonly record struct X11DensityResult(X11DensityOutcome Outcome, string Rule, string? Reason = null, long LaunchId = 0);
