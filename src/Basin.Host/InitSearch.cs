namespace Basin.Host;

/// <summary>
/// The result of <see cref="InitProcess.Find"/>: the executable <c>init</c> that it found, or the reason that it found none.
/// </summary>
/// <param name="Path">The path of the first executable <c>init</c>, or <see langword="null"/> when there is none.</param>
/// <param name="NotExecutable">A value indicating whether an <c>init</c> file exists but is not executable. The caller then stops instead of running without one.</param>
public readonly record struct InitSearch(string? Path, bool NotExecutable);
