namespace Basin.Capabilities;

public interface ITextInputCommit
{
    bool HasActiveTextInput { get; }

    bool TryCommitString(string text);
}
