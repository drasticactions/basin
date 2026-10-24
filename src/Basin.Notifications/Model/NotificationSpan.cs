namespace Basin.Notifications;

/// <summary>
/// A range of the plain body text that carried markup before <see cref="NotificationMarkup.Reduce(string, out NotificationSpan[])"/> removed it.
/// </summary>
/// <param name="Start">The index of the first character in the plain text, in UTF-16 code units.</param>
/// <param name="Length">The number of UTF-16 code units in the range.</param>
/// <param name="Kind">The markup that the range came from.</param>
/// <param name="Target">The link target or image source, or <see langword="null"/> for other kinds or when the attribute is missing.</param>
public readonly record struct NotificationSpan(int Start, int Length, NotificationSpanKind Kind, string? Target = null);
