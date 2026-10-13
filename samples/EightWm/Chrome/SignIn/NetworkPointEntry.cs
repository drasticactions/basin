using System.Windows.Input;

namespace EightWm;

public sealed class NetworkPointEntry(string ssid, string detail, bool enabled, bool active, ICommand choose)
{
    public string Ssid { get; } = ssid;

    public string Detail { get; } = detail;

    public bool Enabled { get; } = enabled;

    public bool Active { get; } = active;

    public ICommand Choose { get; } = choose;

    public override string ToString() => $"{Ssid}, {Detail}";
}
