namespace Basin.Greetd;

/// <summary>
/// The screen that a <see cref="GreeterFlow"/> asks the program to show.
/// </summary>
public enum GreeterPhase
{
    /// <summary>The list of users, with the remembered user as the suggestion.</summary>
    Users,

    /// <summary>A field for a user name that is not in the list.</summary>
    AskingName,

    /// <summary>The prompts and messages of the login for one user.</summary>
    Conversation,

    /// <summary>An error that the person must dismiss.</summary>
    Error,

    /// <summary>The <c>start_session</c> request is outstanding.</summary>
    Starting,

    /// <summary>greetd accepted the session. The program must stop its helpers and exit.</summary>
    Started,
}
