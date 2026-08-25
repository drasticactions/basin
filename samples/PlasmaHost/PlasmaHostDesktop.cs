namespace PlasmaHost;

internal sealed class PlasmaHostDesktop
{
    public PlasmaHostDesktop(ulong id, string name)
    {
        Id = id;
        Name = name;
        Handle = $"plasma-host-desktop-{id}";
    }

    public ulong Id { get; }

    public string Name { get; set; }

    public string Handle { get; }
}
