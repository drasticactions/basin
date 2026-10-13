using System.Text;
using System.Text.Json;
using Basin.Greetd;
using Xunit;

namespace Basin.Tests;

public sealed class GreetdJsonTests
{
    private static string Write(GreetdRequest request) => JsonSerializer.Serialize(request, GreetdJsonContext.Default.GreetdRequest);

    private static GreetdResponse Read(string json) => JsonSerializer.Deserialize(json, GreetdJsonContext.Default.GreetdResponse)!;

    [Fact]
    public void Requests_carry_the_snake_case_tag_and_fields()
    {
        Assert.Equal("""{"type":"create_session","username":"alice"}""", Write(new GreetdCreateSession("alice")));
        Assert.Equal("""{"type":"post_auth_message_response","response":"x"}""", Write(new GreetdPostAuthMessageResponse("x")));
        Assert.Equal("""{"type":"post_auth_message_response"}""", Write(new GreetdPostAuthMessageResponse(null)));
        Assert.Equal("""{"type":"start_session","cmd":["sway"],"env":["A=B"]}""", Write(new GreetdStartSession(["sway"], ["A=B"])));
        Assert.Equal("""{"type":"cancel_session"}""", Write(new GreetdCancelSession()));
    }

    [Fact]
    public void Responses_read_from_the_manual_page_shapes()
    {
        Assert.IsType<GreetdSuccess>(Read("""{"type":"success"}"""));
        var error = Assert.IsType<GreetdError>(Read("""{"type":"error","error_type":"auth_error","description":"nope"}"""));
        Assert.Equal(GreetdErrorType.AuthError, error.ErrorType);
        Assert.Equal("nope", error.Description);
        var message = Assert.IsType<GreetdAuthMessage>(Read("""{"auth_message":"Password:","type":"auth_message","auth_message_type":"secret"}"""));
        Assert.Equal(GreetdAuthMessageType.Secret, message.AuthMessageType);
        Assert.Equal("Password:", message.Message);
        Assert.Equal(GreetdErrorType.Error, Assert.IsType<GreetdError>(Read("""{"type":"error","error_type":"error","description":"d"}""")).ErrorType);
    }

    [Fact]
    public void The_manual_page_hexdump_example_decodes()
    {
        byte[] frame = [.. BitConverter.GetBytes((uint)42), .. Encoding.UTF8.GetBytes("""{"type":"create_session","username":"bob"}""")];
        Assert.Equal(GreetdFrameStatus.Complete, GreetdFrame.TryRead(frame, out var payload, out _));
        var request = Assert.IsType<GreetdCreateSession>(JsonSerializer.Deserialize(payload, GreetdJsonContext.Default.GreetdRequest));
        Assert.Equal("bob", request.Username);
    }

    [Fact]
    public void A_response_field_absent_and_null_both_read_as_no_response()
    {
        Assert.Null(Assert.IsType<GreetdPostAuthMessageResponse>(JsonSerializer.Deserialize("""{"type":"post_auth_message_response"}""", GreetdJsonContext.Default.GreetdRequest)).Response);
        Assert.Null(Assert.IsType<GreetdPostAuthMessageResponse>(JsonSerializer.Deserialize("""{"type":"post_auth_message_response","response":null}""", GreetdJsonContext.Default.GreetdRequest)).Response);
    }

    [Fact]
    public void Invalid_utf8_and_an_empty_payload_are_refused()
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(new byte[] { 0x7b, 0xff, 0x7d }, GreetdJsonContext.Default.GreetdResponse));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(ReadOnlySpan<byte>.Empty, GreetdJsonContext.Default.GreetdResponse));
    }
}
