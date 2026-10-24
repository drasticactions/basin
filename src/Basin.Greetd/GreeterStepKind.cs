namespace Basin.Greetd;

/// <summary>
/// The kind of a <see cref="GreeterStep"/>.
/// </summary>
public enum GreeterStepKind
{
    /// <summary>The flow read the user list. <see cref="GreeterStep.Count"/> is the number of users.</summary>
    UsersRead,

    /// <summary>The flow read the session list. <see cref="GreeterStep.Count"/> is the number of sessions.</summary>
    SessionsRead,

    /// <summary>The flow shows the user list. The text is the suggested user, if any.</summary>
    UsersShown,

    /// <summary>The flow asks for a user name.</summary>
    AskingName,

    /// <summary>A conversation began for a user. The text is the display name.</summary>
    ConversationBegun,

    /// <summary>The flow shows a prompt. The text is the prompt and the prompt type tells whether the answer is secret.</summary>
    Prompt,

    /// <summary>greetd sent an info or error line, and the flow acknowledged it. The text is the line.</summary>
    Message,

    /// <summary>Authentication is complete. The flow starts the session next.</summary>
    LoginOk,

    /// <summary>PAM refused the credentials. The text is the error, or <see langword="null"/> when the flow cannot ask again.</summary>
    AuthFailed,

    /// <summary>greetd sent an error that is not an authentication failure, or the connection closed. The text is the description.</summary>
    Failed,

    /// <summary>Authentication succeeded but no session is available. The text is the error.</summary>
    NoSession,

    /// <summary>greetd accepted the session. The text is the session id.</summary>
    Started,

    /// <summary>The selected session changed. The text is the session id, or <see langword="null"/> when no session exists.</summary>
    SessionSelected,

    /// <summary>A keyboard layout compiled and the seat keyboard uses it. The text is the layout id.</summary>
    LayoutApplied,

    /// <summary>A keyboard layout did not compile, or no keyboard is attached. The keymap did not change. The text is the layout id.</summary>
    LayoutFailed,
}
