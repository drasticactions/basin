namespace Basin.Shell.Weston;

public interface IShellClient
{
    void Configure(Surface surface, uint edges, int width, int height);

    void PrepareLockSurface();

    void GrabCursor(ShellGrabCursor cursor);
}
