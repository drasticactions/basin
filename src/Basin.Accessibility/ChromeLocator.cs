using Tmds.DBus.Protocol;
using static Basin.Accessibility.AccessibilityLog;

namespace Basin.Accessibility;

internal sealed class ChromeLocator : IDisposable
{
    private const string Accessible = "org.a11y.atspi.Accessible";
    private readonly string _sessionAddress;
    private DBusConnection? _a11y;

    public ChromeLocator(string sessionAddress) => _sessionAddress = sessionAddress;

    public async Task<PointerAnswer?> FindAsync(double x, double y)
    {
        try
        {
            var a11y = await ConnectAsync().ConfigureAwait(false);
            var pid = (uint)Environment.ProcessId;
            foreach (var (bus, path) in await ChildrenAsync(a11y, "org.a11y.atspi.Registry", "/org/a11y/atspi/accessible/root").ConfigureAwait(false))
            {
                if (await A11yBus.PidOfAsync(a11y, bus).ConfigureAwait(false) != pid)
                {
                    continue;
                }

                foreach (var (windowBus, window) in await ChildrenAsync(a11y, bus, path).ConfigureAwait(false))
                {
                    var (left, top, width, height) = await ExtentsAsync(a11y, windowBus, window).ConfigureAwait(false);
                    if (x >= left && y >= top && x < left + width && y < top + height)
                    {
                        return new PointerAnswer(0, windowBus, window, x - left, y - top);
                    }
                }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Log.Debug(error, $"pointer: the chrome window under the pointer was not found");
            Interlocked.Exchange(ref _a11y, null)?.Dispose();
        }

        return null;
    }

    public void Dispose() => Interlocked.Exchange(ref _a11y, null)?.Dispose();

    private static async Task<List<(string Bus, string Path)>> ChildrenAsync(DBusConnection a11y, string bus, string path) =>
        await a11y.CallMethodAsync(A11yBus.Call(a11y, bus, path, Accessible, "GetChildren"), static (m, _) =>
        {
            var reader = m.GetBodyReader();
            var children = new List<(string, string)>();
            var array = reader.ReadArrayStart(DBusType.Struct);
            while (reader.HasNext(array))
            {
                reader.AlignStruct();
                children.Add((reader.ReadString(), reader.ReadObjectPathAsString()));
            }

            return children;
        }, null).ConfigureAwait(false);

    private static async Task<(int X, int Y, int Width, int Height)> ExtentsAsync(DBusConnection a11y, string bus, string path)
    {
        return await a11y.CallMethodAsync(ScreenExtents(a11y, bus, path), static (m, _) =>
        {
            var reader = m.GetBodyReader();
            reader.AlignStruct();
            return (reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }, null).ConfigureAwait(false);
    }

    private static MessageBuffer ScreenExtents(DBusConnection connection, string bus, string path)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(bus, path, "org.a11y.atspi.Component", "GetExtents", "u");
        writer.WriteUInt32(0);
        return writer.CreateMessage();
    }

    private async Task<DBusConnection> ConnectAsync()
    {
        if (_a11y is { } existing)
        {
            return existing;
        }

        var a11y = await A11yBus.ConnectAsync(_sessionAddress).ConfigureAwait(false);
        if (Interlocked.CompareExchange(ref _a11y, a11y, null) is { } raced)
        {
            a11y.Dispose();
            return raced;
        }

        return a11y;
    }
}
