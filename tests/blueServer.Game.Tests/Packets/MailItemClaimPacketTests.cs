using blueServer.Game.Packets;
using Xunit;

namespace blueServer.Game.Tests.Packets;

public sealed class MailItemClaimPacketTests
{
    [Fact]
    public void Request_RoundTrip_RestoresRequestId()
    {
        var id = Guid.NewGuid();
        var packet = new MailClaimRequestPacket
        {
            MailId = 20,
            RequestId = id
        }.Serialize();
        var parsed = MailClaimRequestPacket.Read(new PacketReader(packet));

        Assert.Equal(20, parsed.MailId);
        Assert.Equal(id, parsed.RequestId);
    }

    [Fact]
    public void LegacyRequest_RemainsMailIdOnly()
    {
        var packet = new MailClaimRequestPacket
        {
            MailId = 20
        }.Serialize();

        Assert.Equal(12, packet.Length);
        Assert.Null(MailClaimRequestPacket.Read(new PacketReader(packet)).RequestId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx")]
    [InlineData("6c3b9494316a43e594ff3c0d4f456998")]
    public void Request_RejectsMalformedExtension(string id)
    {
        Assert.Throws<PacketProtocolException>(() =>
            MailClaimRequestPacket.Read(new PacketReader(BuildRequest(id))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Request_RejectsTruncatedOrTrailingExtension(bool truncated)
    {
        var bytes = BuildRequest(Guid.NewGuid().ToString("D"));
        Array.Resize(ref bytes, bytes.Length + (truncated ? -1 : 1));
        BitConverter.GetBytes(checked((ushort)bytes.Length)).CopyTo(bytes, 0);

        Assert.Throws<PacketProtocolException>(() =>
            MailClaimRequestPacket.Read(new PacketReader(bytes)));
    }

    [Theory]
    [InlineData(MailClaimPacketStatus.PartiallyClaimed, 999)]
    [InlineData(MailClaimPacketStatus.NoCapacity, 0)]
    public void Result_WritesIncompleteClaimWithoutCompletionTime(
        MailClaimPacketStatus status,
        int granted)
    {
        var id = Guid.NewGuid();
        var processedAt = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        var bytes = new MailClaimResultPacket
        {
            Success = true,
            Status = status,
            Message = "result",
            RequestId = id,
            ProcessedAt = processedAt,
            GrantedGold = 100,
            CurrentGold = 1100,
            CurrentGem = 500,
            Items = [new MailClaimPacketItem(1001, granted, 1, 999999)]
        }.Serialize();
        var reader = new PacketReader(bytes);

        Assert.Equal(Opcode.MailClaimResult, reader.Opcode);
        Assert.True(reader.ReadBool());
        Assert.Equal((int)status, reader.ReadInt());
        Assert.Equal("result", reader.ReadString());
        Assert.False(reader.ReadBool());
        Assert.Equal(1100, reader.ReadInt());
        Assert.Equal(500, reader.ReadInt());
        Assert.Equal(id.ToString("D"), reader.ReadString());
        Assert.True(reader.ReadBool());
        Assert.Equal(
            new DateTimeOffset(processedAt).ToUnixTimeMilliseconds(),
            reader.ReadLong());
        Assert.Equal(100, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(1001, reader.ReadInt());
        Assert.Equal(granted, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(999999, reader.ReadInt());
        reader.EnsureFullyRead();
    }

    [Fact]
    public void Result_RejectsPartialClaimWithCompletionTime()
    {
        var packet = new MailClaimResultPacket
        {
            Success = true,
            Status = MailClaimPacketStatus.PartiallyClaimed,
            RequestId = Guid.NewGuid(),
            ClaimedAt = DateTime.UtcNow
        };

        Assert.Throws<InvalidOperationException>(() => packet.Serialize());
    }

    private static byte[] BuildRequest(string requestId)
    {
        var body = new PacketWriter();
        body.WriteUShort((ushort)Opcode.MailClaim);
        body.WriteLong(20);
        body.WriteString(requestId);
        var frame = new PacketWriter();
        frame.WriteUShort(checked((ushort)(body.ToArray().Length + 2)));
        frame.WriteBytes(body.ToArray());
        return frame.ToArray();
    }
}
