namespace Basin.WindowManager;

/// <summary>
/// The sequence that the compositor is waiting on the manager to finish, which decides the requests that are legal now.
/// </summary>
public enum WmSequence
{
    /// <summary>No sequence is in progress. Sequenced requests throw.</summary>
    None,

    /// <summary>A manage sequence is in progress. Window-management and rendering requests are legal.</summary>
    Manage,

    /// <summary>A render sequence is in progress. Only rendering requests are legal.</summary>
    Render,
}
