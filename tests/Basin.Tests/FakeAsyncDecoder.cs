using Basin.Capabilities;

namespace Basin.Tests;

internal sealed class FakeAsyncDecoder : IVideoDecoder
{
    public List<FakeSession> Sessions { get; } = [];

    public Queue<(FakeSession Session, nint Destination, int Stride, byte[] Packet)> Pending { get; } = new();

    public List<byte[]> Packets { get; } = [];

    public bool Accepts { get; set; } = true;

    public bool Supports(VideoCodec codec) => true;

    public IVideoDecodeSession Open(VideoCodec codec, int width, int height, DrmFormat format)
    {
        var session = new FakeSession(this, width, height);
        Sessions.Add(session);
        return session;
    }

    public void Complete(bool produced = true, byte fill = 0x5c)
    {
        var (session, destination, stride, _) = Pending.Dequeue();
        if (produced)
        {
            unsafe
            {
                for (var y = 0; y < session.Height; y++)
                {
                    new Span<byte>((void*)(destination + (y * stride)), session.Width * 4).Fill(fill);
                }
            }
        }

        session.RaiseCompleted(destination, produced);
    }

    internal sealed class FakeSession : IAsyncVideoDecodeSession
    {
        private readonly FakeAsyncDecoder _owner;

        internal FakeSession(FakeAsyncDecoder owner, int width, int height)
        {
            _owner = owner;
            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }

        public bool Disposed { get; private set; }

        public event Action<nint, bool>? Completed;

        public bool Decode(ReadOnlySpan<byte> packet, nint destination, int stride)
        {
            var bytes = packet.ToArray();
            _owner.Packets.Add(bytes);
            if (!_owner.Accepts)
            {
                return false;
            }

            _owner.Pending.Enqueue((this, destination, stride, bytes));
            return true;
        }

        internal void RaiseCompleted(nint destination, bool produced) => Completed?.Invoke(destination, produced);

        public void Dispose() => Disposed = true;
    }
}
