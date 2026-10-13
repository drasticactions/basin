namespace Basin.Greetd;

public enum GreeterStepKind
{
    UsersRead,

    SessionsRead,

    UsersShown,

    AskingName,

    ConversationBegun,

    Prompt,

    Message,

    LoginOk,

    AuthFailed,

    Failed,

    NoSession,

    Started,

    SessionSelected,

    LayoutApplied,

    LayoutFailed,
}
