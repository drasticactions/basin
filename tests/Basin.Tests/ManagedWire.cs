using System.Buffers.Binary;
using System.Text;

namespace Basin.Tests;

internal static class ManagedWire
{
    public static byte[] Request(uint objectId, int opcode, params object[] args)
    {
        var size = 8;
        foreach (var arg in args)
        {
            size += arg switch
            {
                int or uint => 4,
                string text => 4 + Pad(Encoding.UTF8.GetByteCount(text) + 1),
                _ => throw new NotSupportedException(arg.GetType().Name),
            };
        }

        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, objectId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), ((uint)size << 16) | (uint)opcode);
        var offset = 8;
        foreach (var arg in args)
        {
            switch (arg)
            {
                case int value:
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
                    offset += 4;
                    break;
                case uint value:
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
                    offset += 4;
                    break;
                case string text:
                    var encoded = Encoding.UTF8.GetBytes(text);
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), encoded.Length + 1);
                    offset += 4;
                    encoded.CopyTo(bytes.AsSpan(offset));
                    offset += Pad(encoded.Length + 1);
                    break;
            }
        }

        return bytes;
    }

    public static int ParseInto(List<WireMessage> messages, byte[] pending, int available)
    {
        var offset = 0;
        while (available - offset >= 8)
        {
            var objectId = BinaryPrimitives.ReadUInt32LittleEndian(pending.AsSpan(offset));
            var word = BinaryPrimitives.ReadUInt32LittleEndian(pending.AsSpan(offset + 4));
            var size = (int)(word >> 16);
            if (size < 8 || offset + size > available)
            {
                break;
            }

            messages.Add(new WireMessage(objectId, (int)(word & 0xffff), pending.AsSpan(offset + 8, size - 8).ToArray()));
            offset += size;
        }

        return offset;
    }

    private static int Pad(int length) => (length + 3) & ~3;

    public readonly record struct WireMessage(uint ObjectId, int Opcode, byte[] Body)
    {
        public uint UintAt(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Body.AsSpan(offset));

        public string StringAt(int offset, out int next)
        {
            var length = (int)UintAt(offset);
            next = offset + 4 + Pad(length);
            return Encoding.UTF8.GetString(Body.AsSpan(offset + 4, length - 1));
        }
    }
}
