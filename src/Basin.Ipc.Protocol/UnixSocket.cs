using System.Runtime.InteropServices;
using System.Text;

namespace Basin.Ipc;

internal static unsafe class UnixSocket
{
    public const int EIntr = 4;
    public const int EAgain = 11;
    public const int EConnRefused = 111;
    public const int ENoEnt = 2;
    public const int MaxFds = 16;

    private const int AfUnix = 1;
    private const int SockStream = 1;
    private const int SockNonblock = 0x800;
    private const int SockCloexec = 0x80000;
    private const int SolSocket = 1;
    private const int ScmRights = 1;
    private const int SoPeercred = 17;
    private const int MsgDontwait = 0x40;
    private const int MsgNosignal = 0x4000;
    private const int MsgCmsgCloexec = 0x40000000;
    private const int PathCapacity = 108;
    private const int HeaderBytes = 16;
    private const int FDupfdCloexec = 1030;

    public static int LastError => Marshal.GetLastPInvokeError();

    public static int Create() => socket(AfUnix, SockStream | SockNonblock | SockCloexec, 0);

    public static int Pair(out int first, out int second)
    {
        var fds = stackalloc int[2];
        var result = socketpair(AfUnix, SockStream | SockNonblock | SockCloexec, 0, fds);
        first = fds[0];
        second = fds[1];
        return result;
    }

    public static bool FitsPath(string path) => Encoding.UTF8.GetByteCount(path) < PathCapacity;

    public static int Bind(int fd, string path)
    {
        var address = stackalloc byte[2 + PathCapacity];
        var length = Address(address, path);
        return bind(fd, address, length);
    }

    public static int Connect(int fd, string path)
    {
        var address = stackalloc byte[2 + PathCapacity];
        var length = Address(address, path);
        return connect(fd, address, length);
    }

    public static int Listen(int fd, int backlog) => listen(fd, backlog);

    public static int Accept(int fd) => accept4(fd, 0, 0, SockNonblock | SockCloexec);

    public static bool TryPeerCredentials(int fd, out int pid, out uint uid)
    {
        var credentials = stackalloc int[3];
        var length = 12;
        if (getsockopt(fd, SolSocket, SoPeercred, credentials, &length) != 0)
        {
            pid = 0;
            uid = uint.MaxValue;
            return false;
        }

        pid = credentials[0];
        uid = (uint)credentials[1];
        return true;
    }

    public static nint Receive(int fd, Span<byte> buffer, Span<int> fds, out int fdCount)
    {
        fdCount = 0;
        var control = stackalloc byte[HeaderBytes + (MaxFds * sizeof(int))];
        fixed (byte* data = buffer)
        {
            var vector = new IoVec { Base = data, Length = (nuint)buffer.Length };
            var header = new MessageHeader
            {
                Iov = &vector,
                IovLength = 1,
                Control = control,
                ControlLength = (nuint)(HeaderBytes + (MaxFds * sizeof(int))),
            };

            var read = recvmsg(fd, &header, MsgDontwait | MsgCmsgCloexec);
            if (read < 0 || header.ControlLength < HeaderBytes)
            {
                return read;
            }

            var offset = 0;
            while (offset + HeaderBytes <= (int)header.ControlLength)
            {
                var length = (int)*(nuint*)(control + offset);
                var level = *(int*)(control + offset + 8);
                var type = *(int*)(control + offset + 12);
                if (length < HeaderBytes)
                {
                    break;
                }

                if (level == SolSocket && type == ScmRights)
                {
                    var count = (length - HeaderBytes) / sizeof(int);
                    var received = (int*)(control + offset + HeaderBytes);
                    for (var i = 0; i < count; i++)
                    {
                        if (fdCount < fds.Length)
                        {
                            fds[fdCount++] = received[i];
                        }
                        else
                        {
                            _ = close(received[i]);
                        }
                    }
                }

                offset += (length + 7) & ~7;
            }

            return read;
        }
    }

    public static nint Send(int fd, ReadOnlySpan<byte> data, ReadOnlySpan<int> fds)
    {
        if (fds.Length > MaxFds)
        {
            throw new ArgumentOutOfRangeException(nameof(fds), $"at most {MaxFds} descriptors go on one message");
        }

        var control = stackalloc byte[HeaderBytes + (MaxFds * sizeof(int))];
        fixed (byte* bytes = data)
        {
            var vector = new IoVec { Base = bytes, Length = (nuint)data.Length };
            var header = new MessageHeader { Iov = &vector, IovLength = 1 };
            if (!fds.IsEmpty)
            {
                var length = HeaderBytes + (fds.Length * sizeof(int));
                *(nuint*)control = (nuint)length;
                *(int*)(control + 8) = SolSocket;
                *(int*)(control + 12) = ScmRights;
                fds.CopyTo(new Span<int>(control + HeaderBytes, fds.Length));
                header.Control = control;
                header.ControlLength = (nuint)((length + 7) & ~7);
            }

            return sendmsg(fd, &header, MsgDontwait | MsgNosignal);
        }
    }

    public static int Close(int fd) => close(fd);

    public static int Duplicate(int fd) => fcntl(fd, FDupfdCloexec, 0);

    public static int Unlink(string path)
    {
        var bytes = stackalloc byte[Encoding.UTF8.GetByteCount(path) + 1];
        Terminated(bytes, path);
        return unlink(bytes);
    }

    public static int Chmod(string path, uint mode)
    {
        var bytes = stackalloc byte[Encoding.UTF8.GetByteCount(path) + 1];
        Terminated(bytes, path);
        return chmod(bytes, mode);
    }

    public static uint EffectiveUid() => geteuid();

    private static int Address(byte* address, string path)
    {
        if (!FitsPath(path))
        {
            throw new ArgumentException($"'{path}' is longer than a Unix socket path can be", nameof(path));
        }

        new Span<byte>(address, 2 + PathCapacity).Clear();
        *(ushort*)address = AfUnix;
        var written = Encoding.UTF8.GetBytes(path, new Span<byte>(address + 2, PathCapacity));
        return 2 + written + 1;
    }

    private static void Terminated(byte* into, string text)
    {
        var length = Encoding.UTF8.GetByteCount(text);
        _ = Encoding.UTF8.GetBytes(text, new Span<byte>(into, length));
        into[length] = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoVec
    {
        public byte* Base;
        public nuint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MessageHeader
    {
        public void* Name;
        public uint NameLength;
        public IoVec* Iov;
        public nuint IovLength;
        public void* Control;
        public nuint ControlLength;
        public int Flags;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int socket(int domain, int type, int protocol);

    [DllImport("libc", SetLastError = true)]
    private static extern int socketpair(int domain, int type, int protocol, int* fds);

    [DllImport("libc", SetLastError = true)]
    private static extern int bind(int fd, byte* address, int length);

    [DllImport("libc", SetLastError = true)]
    private static extern int connect(int fd, byte* address, int length);

    [DllImport("libc", SetLastError = true)]
    private static extern int listen(int fd, int backlog);

    [DllImport("libc", SetLastError = true)]
    private static extern int accept4(int fd, nint address, nint length, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int getsockopt(int fd, int level, int name, void* value, int* length);

    [DllImport("libc", SetLastError = true)]
    private static extern nint recvmsg(int fd, MessageHeader* message, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern nint sendmsg(int fd, MessageHeader* message, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int unlink(byte* path);

    [DllImport("libc", SetLastError = true)]
    private static extern int chmod(byte* path, uint mode);

    [DllImport("libc")]
    private static extern uint geteuid();

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int command, int argument);
}
