namespace Basin.Capabilities;

public interface ITextInputFocus : ITextInputCommit
{
    void NotifyFocus(Surface? surface);
}
