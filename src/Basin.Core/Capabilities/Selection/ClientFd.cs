using System.Runtime.InteropServices;
using Wayland.Server;

namespace Basin;

public readonly struct ClientFd
{
    public ClientFd(int value, WlClient? owner)
    {
        Value = value;
        Owner = owner;
    }

    public int Value { get; }

    public WlClient? Owner { get; }

    public void Close()
    {
        if (Value < 0)
        {
            return;
        }

        if (Owner is { } owner)
        {
            owner.CloseFd(Value);
        }
        else
        {
            _ = close(Value);
        }
    }

    [DllImport("libc", EntryPoint = "close", ExactSpelling = true)]
    private static extern int close(int fd);
}
