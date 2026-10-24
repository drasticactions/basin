namespace Basin.Capabilities;

/// <summary>
/// The result of a density change that <see cref="IX11DensityControl.TrySet"/> started.
/// </summary>
/// <param name="Outcome">What the change did.</param>
/// <param name="Rule">The launch rule that was written, in its configuration form.</param>
/// <param name="Reason">A readable reason for an outcome other than <see cref="X11DensityOutcome.Relaunched"/>, or <see langword="null"/>.</param>
/// <param name="LaunchId">The launch id of the new process after a relaunch, otherwise 0.</param>
public readonly record struct X11DensityResult(X11DensityOutcome Outcome, string Rule, string? Reason = null, long LaunchId = 0);
