namespace Basin.Greetd;

/// <summary>
/// The result of <see cref="GreetdFrame.TryRead"/>.
/// </summary>
public enum GreetdFrameStatus
{
    /// <summary>The buffer holds a whole frame at its start.</summary>
    Complete,
    /// <summary>The buffer does not hold a whole frame yet. The reader must wait for more bytes.</summary>
    Incomplete,
    /// <summary>The header gives a length over <see cref="GreetdFrame.MaxLength"/>. The reader must close the connection.</summary>
    TooLarge,
}
