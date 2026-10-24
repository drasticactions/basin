using System.Runtime.InteropServices;
using Wayland.Server;

namespace Basin;

/// <summary>
/// Holds a file descriptor that a client sent or receives, with the client whose transport it belongs to. On a managed transport, the value is a slot number and not a kernel descriptor.
/// </summary>
public readonly struct ClientFd
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClientFd"/> struct.
    /// </summary>
    /// <param name="value">The descriptor or the slot number. A negative value means no descriptor.</param>
    /// <param name="owner">The client whose transport owns the value, or <see langword="null"/> for a kernel descriptor that the process owns.</param>
    public ClientFd(int value, WlClient? owner)
    {
        Value = value;
        Owner = owner;
    }

    /// <summary>
    /// Gets the descriptor or the slot number. It is negative when there is no descriptor.
    /// </summary>
    public int Value { get; }

    /// <summary>
    /// Gets the client whose transport owns <see cref="Value"/>, or <see langword="null"/> for a plain kernel descriptor.
    /// </summary>
    public WlClient? Owner { get; }

    /// <summary>
    /// Closes the descriptor through its owner, or with <c>close(2)</c> when there is no owner. A negative value does nothing.
    /// </summary>
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
