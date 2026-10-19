namespace Basin.Ipc;

public abstract class IpcEventSource
{
    public bool IsAttached { get; private set; }

    internal int Demand { get; set; }

    internal void SetAttached(bool attached)
    {
        if (attached == IsAttached)
        {
            return;
        }

        IsAttached = attached;
        if (attached)
        {
            Attach();
        }
        else
        {
            Detach();
        }
    }

    protected internal virtual string? Validate(string name, System.Text.Json.JsonElement? options) => null;

    protected internal virtual void SubscribersChanged(string name)
    {
    }

    protected abstract void Attach();

    protected abstract void Detach();
}
