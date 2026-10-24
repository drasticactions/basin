namespace Basin.SoftKeyboard;

/// <summary>
/// One word that <see cref="TraceDecoder"/> found for a trace path.
/// </summary>
/// <param name="Word">The candidate word.</param>
/// <param name="Score">The log score. A higher score is a better match. The value is not a probability.</param>
public readonly record struct TraceCandidate(string Word, double Score);
