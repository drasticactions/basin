namespace BasinGreeter.UI;

public sealed class SessionEntry(string id, string name)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public override string ToString() => Name;
}
