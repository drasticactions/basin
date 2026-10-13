using System.Runtime.InteropServices;
using System.Text;
using Basin.Greetd;
using Xunit;

namespace Basin.Tests;

public sealed class GreetdFrameTests
{
    [Fact]
    public void The_length_is_in_native_order_and_round_trips()
    {
        using var writer = new GreetdFrameWriter();
        writer.Write(new GreetdCreateSession("da"));
        var frame = writer.Frame.ToArray();
        Assert.Equal((uint)(frame.Length - 4), MemoryMarshal.Read<uint>(frame));
        Assert.Equal((uint)(frame.Length - 4), BitConverter.ToUInt32(frame));
        Assert.Equal(GreetdFrameStatus.Complete, GreetdFrame.TryRead(frame, out var payload, out var consumed));
        Assert.Equal(frame.Length, consumed);
        Assert.Equal("""{"type":"create_session","username":"da"}""", Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void A_split_frame_is_incomplete_until_the_last_byte()
    {
        using var writer = new GreetdFrameWriter();
        writer.Write(new GreetdSuccess());
        var frame = writer.Frame.ToArray();
        for (var length = 0; length < frame.Length; length++)
        {
            Assert.Equal(GreetdFrameStatus.Incomplete, GreetdFrame.TryRead(frame.AsSpan(0, length), out _, out _));
        }

        Assert.Equal(GreetdFrameStatus.Complete, GreetdFrame.TryRead(frame, out _, out _));
    }

    [Fact]
    public void A_frame_over_the_cap_is_refused_and_a_zero_length_frame_reads()
    {
        var big = new byte[8];
        GreetdFrame.WriteHeader(big, GreetdFrame.MaxLength + 1);
        Assert.Equal(GreetdFrameStatus.TooLarge, GreetdFrame.TryRead(big, out _, out _));
        var empty = new byte[4];
        Assert.Equal(GreetdFrameStatus.Complete, GreetdFrame.TryRead(empty, out var payload, out var consumed));
        Assert.Equal(0, payload.Length);
        Assert.Equal(4, consumed);
    }

    [Fact]
    public void The_auth_response_is_written_from_bytes_and_cleared_after()
    {
        using var writer = new GreetdFrameWriter();
        writer.WriteAuthResponse("hunter\"2"u8);
        Assert.Equal("{\"type\":\"post_auth_message_response\",\"response\":\"hunter\\u00222\"}", Encoding.UTF8.GetString(writer.Payload));
        writer.Clear();
        Assert.Equal(0, writer.Payload.Length);
        Assert.Equal(4, writer.Frame.Length);
    }
}
