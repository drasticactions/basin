namespace Basin.Capabilities;

public interface IKeyText
{
    int TextFor(uint key, Span<char> into);
}
