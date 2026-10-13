namespace EightWm;

public sealed class SignInOption(string id, string name) : ObservableModel
{
    private bool _chosen;

    public string Id { get; } = id;

    public string Name { get; } = name;

    public bool Chosen
    {
        get => _chosen;
        set => Set(ref _chosen, value);
    }
}
