namespace Basin.Capabilities;

public interface IAppInfoResolver
{
    bool TryResolve(string appId, out AppInfo info);
}
