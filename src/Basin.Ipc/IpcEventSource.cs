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

    protected abstract void Attach();

    protected abstract void Detach();
}
