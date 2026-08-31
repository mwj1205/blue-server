using blueServer.Game.Packets;
using blueServer.Infrastructure.Mails;
using Xunit;

namespace blueServer.Game.Tests.Packets;

public sealed class MailRequestPacketTests
{
    private static readonly DateTime SentAt = new(
        2026,
        8,
        30,
        1,
        2,
        3,
        456,
        DateTimeKind.Utc);

    [Fact]
    public void MailListRequestPacket_Read_RestoresPageAndCursor()
    {
        var packet = new MailListRequestPacket
        {
            PageSize = 25,
            Cursor = new MailListCursor(SentAt, 99)
        }.Serialize();

        var reader = new PacketReader(packet);
        var request = MailListRequestPacket.Read(reader);

        Assert.Equal(Opcode.MailList, reader.Opcode);
        Assert.Equal(25, request.PageSize);
        Assert.NotNull(request.Cursor);
        Assert.Equal(SentAt, request.Cursor.SentAt);
        Assert.Equal(99, request.Cursor.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void MailListRequestPacket_Read_Throws_WhenPageSizeIsInvalid(
        int pageSize)
    {
        var packet = new MailListRequestPacket
        {
            PageSize = pageSize
        }.Serialize();

        var reader = new PacketReader(packet);

        Assert.Throws<PacketProtocolException>(() =>
            MailListRequestPacket.Read(reader));
    }

    [Fact]
    public void MailListRequestPacket_Read_Throws_WhenCursorIdIsInvalid()
    {
        var packet = new MailListRequestPacket
        {
            PageSize = 20,
            Cursor = new MailListCursor(SentAt, 0)
        }.Serialize();

        var reader = new PacketReader(packet);

        Assert.Throws<PacketProtocolException>(() =>
            MailListRequestPacket.Read(reader));
    }

    [Fact]
    public void MailDetailRequestPacket_Read_Throws_WhenPayloadHasTrailingBytes()
    {
        var bodyWriter = new PacketWriter();
        bodyWriter.WriteUShort((ushort)Opcode.MailDetail);
        bodyWriter.WriteLong(10);
        bodyWriter.WriteBool(true);

        var body = bodyWriter.ToArray();
        var packetWriter = new PacketWriter();
        packetWriter.WriteUShort(checked((ushort)(body.Length + 2)));
        packetWriter.WriteBytes(body);

        var reader = new PacketReader(packetWriter.ToArray());

        Assert.Throws<PacketProtocolException>(() =>
            MailDetailRequestPacket.Read(reader));
    }

    [Fact]
    public void MailDetailRequestPacket_Read_RestoresMailId()
    {
        var packet = new MailDetailRequestPacket
        {
            MailId = 10
        }.Serialize();

        var reader = new PacketReader(packet);
        var request = MailDetailRequestPacket.Read(reader);

        Assert.Equal(Opcode.MailDetail, reader.Opcode);
        Assert.Equal(10, request.MailId);
    }

    [Fact]
    public void MailClaimRequestPacket_Read_RestoresMailId()
    {
        var packet = new MailClaimRequestPacket
        {
            MailId = 20
        }.Serialize();

        var reader = new PacketReader(packet);
        var request = MailClaimRequestPacket.Read(reader);

        Assert.Equal(Opcode.MailClaim, reader.Opcode);
        Assert.Equal(20, request.MailId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MailClaimRequestPacket_Read_Throws_WhenMailIdIsInvalid(
        long mailId)
    {
        var packet = new MailClaimRequestPacket
        {
            MailId = mailId
        }.Serialize();
        var reader = new PacketReader(packet);

        Assert.Throws<PacketProtocolException>(() =>
            MailClaimRequestPacket.Read(reader));
    }

    [Fact]
    public void MailClaimAllRequestPacket_Read_AcceptsEmptyPayload()
    {
        var packet = new MailClaimAllRequestPacket().Serialize();
        var reader = new PacketReader(packet);

        MailClaimAllRequestPacket.Read(reader);

        Assert.Equal(Opcode.MailClaimAll, reader.Opcode);
    }

    [Fact]
    public void MailClaimAllRequestPacket_Read_Throws_WhenPayloadHasTrailingBytes()
    {
        var bodyWriter = new PacketWriter();
        bodyWriter.WriteUShort((ushort)Opcode.MailClaimAll);
        bodyWriter.WriteBool(true);

        var body = bodyWriter.ToArray();
        var packetWriter = new PacketWriter();
        packetWriter.WriteUShort(checked((ushort)(body.Length + 2)));
        packetWriter.WriteBytes(body);
        var reader = new PacketReader(packetWriter.ToArray());

        Assert.Throws<PacketProtocolException>(() =>
            MailClaimAllRequestPacket.Read(reader));
    }
}
