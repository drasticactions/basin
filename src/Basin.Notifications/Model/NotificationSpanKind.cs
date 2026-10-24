namespace Basin.Notifications;

/// <summary>
/// The markup that a <see cref="NotificationSpan"/> came from.
/// </summary>
public enum NotificationSpanKind
{
    /// <summary>A <c>b</c> element.</summary>
    Bold,
    /// <summary>An <c>i</c> element.</summary>
    Italic,
    /// <summary>A <c>u</c> element.</summary>
    Underline,
    /// <summary>An <c>a</c> element. <see cref="NotificationSpan.Target"/> is its <c>href</c>.</summary>
    Link,
    /// <summary>An <c>img</c> element. The span covers its alt text, and <see cref="NotificationSpan.Target"/> is its <c>src</c>.</summary>
    Image,
}
