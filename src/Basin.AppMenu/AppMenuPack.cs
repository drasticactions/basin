namespace Basin.AppMenu;

/// <summary>The protocol packs of <c>Basin.AppMenu</c>.</summary>
public static class AppMenuPack
{
    /// <summary>
    /// Gets a new pack with <see cref="KdeAppMenuModule"/> and <see cref="GtkShellModule"/>. Each call returns new module instances.
    /// </summary>
    public static ProtocolPack Default => new(
    [
        new KdeAppMenuModule(),
        new GtkShellModule(),
    ]);
}
