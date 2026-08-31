using blueServer.Game.Packets;
using blueServer.Infrastructure.Mails;
using Xunit;

namespace blueServer.Game.Tests.Packets;

public sealed class MailResultPacketTests
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
    public void MailListResultPacket_Serialize_WritesExpectedPayload()
    {
        var expiresAt = SentAt.AddDays(7);
        var packet = new MailListResultPacket
        {
            Success = true,
            Message = "Mail list loaded",
            Items =
            [
                new MailListPacketItem(
                    10,
                    "Attendance reward",
                    SentAt,
                    expiresAt,
                    true,
                    false,
                    false,
                    true,
                    2)
            ],
            NextCursor = new MailListCursor(SentAt, 10)
        }.Serialize();

        var reader = new PacketReader(packet);

        Assert.Equal(Opcode.MailListResult, reader.Opcode);
        Assert.True(reader.ReadBool());
        Assert.Equal("Mail list loaded", reader.ReadString());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(10, reader.ReadLong());
        Assert.Equal("Attendance reward", reader.ReadString());
        Assert.Equal(ToUnixMilliseconds(SentAt), reader.ReadLong());
        Assert.True(reader.ReadBool());
        Assert.Equal(ToUnixMilliseconds(expiresAt), reader.ReadLong());
        Assert.True(reader.ReadBool());
        Assert.False(reader.ReadBool());
        Assert.False(reader.ReadBool());
        Assert.True(reader.ReadBool());
        Assert.Equal(2, reader.ReadInt());
        Assert.True(reader.ReadBool());
        Assert.Equal(ToUnixMilliseconds(SentAt), reader.ReadLong());
        Assert.Equal(10, reader.ReadLong());
        reader.EnsureFullyRead();
    }

    [Fact]
    public void MailDetailResultPacket_Serialize_WritesExpectedPayload()
    {
        var readAt = SentAt.AddMinutes(1);
        var packet = new MailDetailResultPacket
        {
            Success = true,
            Message = "Mail detail loaded",
            Mail = new MailDetailPacketItem(
                10,
                "Attendance reward",
                "Daily login reward",
                SentAt,
                null,
                readAt,
                null,
                false,
                true,
                [
                    new MailAttachmentPacketItem(1, 100),
                    new MailAttachmentPacketItem(2, 10)
                ])
        }.Serialize();

        var reader = new PacketReader(packet);

        Assert.Equal(Opcode.MailDetailResult, reader.Opcode);
        Assert.True(reader.ReadBool());
        Assert.Equal("Mail detail loaded", reader.ReadString());
        Assert.Equal(10, reader.ReadLong());
        Assert.Equal("Attendance reward", reader.ReadString());
        Assert.Equal("Daily login reward", reader.ReadString());
        Assert.Equal(ToUnixMilliseconds(SentAt), reader.ReadLong());
        Assert.False(reader.ReadBool());
        Assert.True(reader.ReadBool());
        Assert.Equal(ToUnixMilliseconds(readAt), reader.ReadLong());
        Assert.False(reader.ReadBool());
        Assert.False(reader.ReadBool());
        Assert.True(reader.ReadBool());
        Assert.Equal(2, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(100, reader.ReadInt());
        Assert.Equal(2, reader.ReadInt());
        Assert.Equal(10, reader.ReadInt());
        reader.EnsureFullyRead();
    }

    [Fact]
    public void MailDetailResultPacket_Serialize_Throws_WhenSuccessHasNoMail()
    {
        var packet = new MailDetailResultPacket
        {
            Success = true,
            Message = "Mail detail loaded"
        };

        Assert.Throws<InvalidOperationException>(() =>
            packet.Serialize());
    }

    [Fact]
    public void MailClaimResultPacket_Serialize_WritesSuccessfulResult()
    {
        var packet = new MailClaimResultPacket
        {
            Success = true,
            Status = MailClaimPacketStatus.Claimed,
            Message = "Mail rewards claimed",
            ClaimedAt = SentAt,
            CurrentGold = 1120,
            CurrentGem = 515
        }.Serialize();

        var reader = new PacketReader(packet);

        Assert.Equal(Opcode.MailClaimResult, reader.Opcode);
        Assert.True(reader.ReadBool());
        Assert.Equal(
            (int)MailClaimPacketStatus.Claimed,
            reader.ReadInt());
        Assert.Equal("Mail rewards claimed", reader.ReadString());
        Assert.True(reader.ReadBool());
        Assert.Equal(ToUnixMilliseconds(SentAt), reader.ReadLong());
        Assert.Equal(1120, reader.ReadInt());
        Assert.Equal(515, reader.ReadInt());
        reader.EnsureFullyRead();
    }

    [Fact]
    public void MailClaimResultPacket_Serialize_WritesFailureResult()
    {
        var packet = new MailClaimResultPacket
        {
            Success = false,
            Status = MailClaimPacketStatus.Expired,
            Message = "Mail has expired"
        }.Serialize();

        var reader = new PacketReader(packet);

        Assert.Equal(Opcode.MailClaimResult, reader.Opcode);
        Assert.False(reader.ReadBool());
        Assert.Equal(
            (int)MailClaimPacketStatus.Expired,
            reader.ReadInt());
        Assert.Equal("Mail has expired", reader.ReadString());
        Assert.False(reader.ReadBool());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        reader.EnsureFullyRead();
    }

    [Fact]
    public void MailClaimResultPacket_Serialize_Throws_WhenSuccessHasNoClaimTime()
    {
        var packet = new MailClaimResultPacket
        {
            Success = true,
            Status = MailClaimPacketStatus.Claimed,
            Message = "Mail rewards claimed"
        };

        Assert.Throws<InvalidOperationException>(() =>
            packet.Serialize());
    }

    [Fact]
    public void MailClaimAllResultPacket_Serialize_WritesClaimedResult()
    {
        var packet = new MailClaimAllResultPacket
        {
            Success = true,
            Status = MailClaimAllPacketStatus.Claimed,
            Message = "Mail rewards claimed",
            ClaimedMailCount = 2,
            GrantedGold = 150,
            GrantedGem = 10,
            CurrentGold = 1150,
            CurrentGem = 510,
            HasMore = true
        }.Serialize();

        var reader = new PacketReader(packet);

        Assert.Equal(Opcode.MailClaimAllResult, reader.Opcode);
        Assert.True(reader.ReadBool());
        Assert.Equal(
            (int)MailClaimAllPacketStatus.Claimed,
            reader.ReadInt());
        Assert.Equal("Mail rewards claimed", reader.ReadString());
        Assert.Equal(2, reader.ReadInt());
        Assert.Equal(150, reader.ReadInt());
        Assert.Equal(10, reader.ReadInt());
        Assert.Equal(1150, reader.ReadInt());
        Assert.Equal(510, reader.ReadInt());
        Assert.True(reader.ReadBool());
        reader.EnsureFullyRead();
    }

    [Fact]
    public void MailClaimAllResultPacket_Serialize_WritesNothingToClaimResult()
    {
        var packet = new MailClaimAllResultPacket
        {
            Success = true,
            Status = MailClaimAllPacketStatus.NothingToClaim,
            Message = "No Mail rewards to claim",
            CurrentGold = 1000,
            CurrentGem = 500
        }.Serialize();

        var reader = new PacketReader(packet);

        Assert.Equal(Opcode.MailClaimAllResult, reader.Opcode);
        Assert.True(reader.ReadBool());
        Assert.Equal(
            (int)MailClaimAllPacketStatus.NothingToClaim,
            reader.ReadInt());
        Assert.Equal("No Mail rewards to claim", reader.ReadString());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(1000, reader.ReadInt());
        Assert.Equal(500, reader.ReadInt());
        Assert.False(reader.ReadBool());
        reader.EnsureFullyRead();
    }

    [Fact]
    public void MailClaimAllResultPacket_Serialize_Throws_WhenClaimedCountIsZero()
    {
        var packet = new MailClaimAllResultPacket
        {
            Success = true,
            Status = MailClaimAllPacketStatus.Claimed,
            Message = "Mail rewards claimed",
            CurrentGold = 1000,
            CurrentGem = 500
        };

        Assert.Throws<InvalidOperationException>(() =>
            packet.Serialize());
    }

    [Fact]
    public void MailReadResultPacket_Serialize_WritesMarkedAsReadResult()
    {
        var packet = new MailReadResultPacket
        {
            Success = true,
            Status = MailReadPacketStatus.MarkedAsRead,
            Message = "Mail marked as read",
            ReadAt = SentAt
        }.Serialize();

        var reader = new PacketReader(packet);

        Assert.Equal(Opcode.MailReadResult, reader.Opcode);
        Assert.True(reader.ReadBool());
        Assert.Equal(
            (int)MailReadPacketStatus.MarkedAsRead,
            reader.ReadInt());
        Assert.Equal("Mail marked as read", reader.ReadString());
        Assert.True(reader.ReadBool());
        Assert.Equal(ToUnixMilliseconds(SentAt), reader.ReadLong());
        reader.EnsureFullyRead();
    }

    [Fact]
    public void MailReadResultPacket_Serialize_WritesNotFoundResult()
    {
        var packet = new MailReadResultPacket
        {
            Success = false,
            Status = MailReadPacketStatus.NotFound,
            Message = "Mail not found"
        }.Serialize();

        var reader = new PacketReader(packet);

        Assert.Equal(Opcode.MailReadResult, reader.Opcode);
        Assert.False(reader.ReadBool());
        Assert.Equal(
            (int)MailReadPacketStatus.NotFound,
            reader.ReadInt());
        Assert.Equal("Mail not found", reader.ReadString());
        Assert.False(reader.ReadBool());
        reader.EnsureFullyRead();
    }

    [Fact]
    public void MailReadResultPacket_Serialize_Throws_WhenSuccessHasNoReadTime()
    {
        var packet = new MailReadResultPacket
        {
            Success = true,
            Status = MailReadPacketStatus.MarkedAsRead,
            Message = "Mail marked as read"
        };

        Assert.Throws<InvalidOperationException>(() =>
            packet.Serialize());
    }

    private static long ToUnixMilliseconds(DateTime value)
    {
        return new DateTimeOffset(value).ToUnixTimeMilliseconds();
    }
}
