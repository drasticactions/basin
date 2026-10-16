using Tmds.DBus.Protocol;
using Xunit;

namespace BasinGreeter.Tests;

internal static class AtSpiTree
{
    public static async Task<List<string>> Texts(DBusConnection a11y)
    {
        var texts = new List<string>();
        foreach (var (service, path) in await Children(a11y, "org.a11y.atspi.Registry", "/org/a11y/atspi/accessible/root"))
        {
            await CollectTexts(a11y, service, path, 0, texts);
        }

        return texts;
    }

    private static async Task CollectTexts(DBusConnection a11y, string service, string path, int depth, List<string> texts)
    {
        if (depth > 40)
        {
            return;
        }

        try
        {
            texts.Add(await a11y.CallMethodAsync(GetText(a11y, service, path), static (m, _) => m.GetBodyReader().ReadString(), null));
        }
        catch (DBusErrorReplyException)
        {
        }

        foreach (var (childService, childPath) in await Children(a11y, service, path))
        {
            await CollectTexts(a11y, childService, childPath, depth + 1, texts);
        }
    }

    private static MessageBuffer GetText(DBusConnection connection, string service, string path)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(service, path, "org.a11y.atspi.Text", "GetText", "ii");
        writer.WriteInt32(0);
        writer.WriteInt32(-1);
        return writer.CreateMessage();
    }

    public static async Task<List<(string Role, string Name)>> Where(DBusConnection a11y, Func<List<(string Role, string Name)>, bool> done)
    {
        var nodes = new List<(string Role, string Name)>();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            nodes = await Tree(a11y);
            if (done(nodes))
            {
                break;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.True(done(nodes), "tree: " + string.Join(", ", nodes.Select(node => $"{node.Role}:{node.Name}")));
        return nodes;
    }

    public static async Task<List<(string Role, string Name)>> Tree(DBusConnection a11y)
    {
        var found = new List<(string, string)>();
        var applications = await Children(a11y, "org.a11y.atspi.Registry", "/org/a11y/atspi/accessible/root");
        foreach (var (service, path) in applications)
        {
            await Walk(a11y, service, path, 0, found);
        }

        return found;
    }

    private static async Task Walk(DBusConnection a11y, string service, string path, int depth, List<(string, string)> found)
    {
        if (depth > 40)
        {
            return;
        }

        string role, name;
        try
        {
            role = await a11y.CallMethodAsync(Call(a11y, service, path, "org.a11y.atspi.Accessible", "GetRoleName"), static (m, _) => m.GetBodyReader().ReadString(), null);
            name = await a11y.CallMethodAsync(GetName(a11y, service, path), static (m, _) => m.GetBodyReader().ReadVariantValue().GetString(), null);
        }
        catch (DBusErrorReplyException)
        {
            return;
        }

        found.Add((role, name));
        foreach (var (childService, childPath) in await Children(a11y, service, path))
        {
            await Walk(a11y, childService, childPath, depth + 1, found);
        }
    }

    private static async Task<List<(string, string)>> Children(DBusConnection a11y, string service, string path)
    {
        try
        {
            return await a11y.CallMethodAsync(Call(a11y, service, path, "org.a11y.atspi.Accessible", "GetChildren"), static (m, _) =>
            {
                var reader = m.GetBodyReader();
                var children = new List<(string, string)>();
                var array = reader.ReadArrayStart(DBusType.Struct);
                while (reader.HasNext(array))
                {
                    reader.AlignStruct();
                    children.Add((reader.ReadString(), reader.ReadObjectPath().ToString()));
                }

                return children;
            }, null);
        }
        catch (DBusErrorReplyException)
        {
            return [];
        }
    }

    private static MessageBuffer Call(DBusConnection connection, string service, string path, string @interface, string member)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(service, path, @interface, member);
        return writer.CreateMessage();
    }

    private static MessageBuffer GetName(DBusConnection connection, string service, string path)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(service, path, "org.freedesktop.DBus.Properties", "Get", "ss");
        writer.WriteString("org.a11y.atspi.Accessible");
        writer.WriteString("Name");
        return writer.CreateMessage();
    }
}
