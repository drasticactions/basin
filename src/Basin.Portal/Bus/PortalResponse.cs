namespace Basin.Portal;

/// <summary>
/// The response codes that a portal method returns with its results.
/// </summary>
public static class PortalResponse
{
    /// <summary>
    /// The user accepted and the results are valid.
    /// </summary>
    public const uint Success = 0;

    /// <summary>
    /// The user canceled the interaction.
    /// </summary>
    public const uint Cancelled = 1;

    /// <summary>
    /// The interaction ended in another way, for example a closed request or a failure.
    /// </summary>
    public const uint Other = 2;
}
