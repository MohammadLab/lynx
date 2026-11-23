using Lynx.Protocol;

namespace Lynx.Tests;

public sealed class PingCodecTests
{
    [Fact]
    public void SerializeThenParse_PreservesPing()
    {
        var original = new PingMessage(
            Id: 12345,
            SenderId: 7,
            BeaconId: 0,
            X: 1.25f,
            Y: -0.5f,
            Z: 3.75f,
            CreatedAt: 1789345731115);

        var bytes = PingCodec.Serialize(original);

        var ok = PingCodec.TryParse(bytes, out var parsed, out var error);

        Assert.True(ok, error);
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void MessageCodec_AcceptsAndroidCreateEnvelope()
    {
        var bytes =
            """{"type":"PING_CREATE","ping":{"id":1789345731115,"senderId":1,"beaconId":0,"x":-0.21964669,"y":-0.775046,"z":1.9151106,"createdAt":1789345731115}}"""u8.ToArray();

        var ok = LynxMessageCodec.TryParse(
            bytes,
            out var message,
            out var error);

        Assert.True(ok, error);
        Assert.NotNull(message);
        Assert.Equal(LynxMessageTypes.PingCreate, message.Type);
        Assert.NotNull(message.Ping);
        Assert.Equal(1789345731115L, message.Ping.Id);
        Assert.Equal(1L, message.Ping.SenderId);
    }

    [Fact]
    public void MessageCodec_AcceptsAndroidRemoveEnvelope()
    {
        var bytes =
            """{"type":"PING_REMOVE","pingId":1789345731115,"senderId":1}"""u8.ToArray();

        var ok = LynxMessageCodec.TryParse(
            bytes,
            out var message,
            out var error);

        Assert.True(ok, error);
        Assert.NotNull(message);
        Assert.Equal(LynxMessageTypes.PingRemove, message.Type);
        Assert.Equal(1789345731115L, message.PingId);
        Assert.Equal(1L, message.SenderId);
    }

    [Fact]
    public void MessageCodec_RejectsUnknownType()
    {
        var bytes = """{"type":"NOPE"}"""u8.ToArray();

        var ok = LynxMessageCodec.TryParse(
            bytes,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("Unknown message type", error);
    }

    [Fact]
    public void MessageCodec_RejectsNegativeBeaconId()
    {
        var bytes =
            """{"type":"PING_CREATE","ping":{"id":1,"senderId":1,"beaconId":-1,"x":0,"y":0,"z":0,"createdAt":1000}}"""u8.ToArray();

        var ok = LynxMessageCodec.TryParse(
            bytes,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("beaconId", error);
    }

    [Fact]
    public void MessageCodec_RejectsMalformedJson()
    {
        var bytes = "not-json"u8.ToArray();

        var ok = LynxMessageCodec.TryParse(
            bytes,
            out var message,
            out var error);

        Assert.False(ok);
        Assert.Null(message);
        Assert.Contains("Invalid JSON", error);
    }

    [Fact]
    public void MessageCodec_RejectsCreateWithoutPing()
    {
        var bytes =
            """{"type":"PING_CREATE"}"""u8.ToArray();

        var ok = LynxMessageCodec.TryParse(
            bytes,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("requires ping", error);
    }

    [Fact]
    public void MessageCodec_RejectsRemoveWithoutSender()
    {
        var bytes =
            """{"type":"PING_REMOVE","pingId":1}"""u8.ToArray();

        var ok = LynxMessageCodec.TryParse(
            bytes,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("senderId greater than zero", error);
    }
}
