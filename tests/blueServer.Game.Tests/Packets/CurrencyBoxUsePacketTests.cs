using blueServer.Domain.Currencies;
using blueServer.Game.Packets;
using Xunit;

namespace blueServer.Game.Tests.Packets;

public sealed class CurrencyBoxUsePacketTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(999999)]
    public void Request_RoundTrip_RestoresFields(int quantity)
    {
        var id = Guid.NewGuid();
        var bytes = new CurrencyBoxUseRequestPacket
        {
            ItemTemplateId = 3103,
            Quantity = quantity,
            RequestId = id
        }.Serialize();
        Assert.Equal(50, bytes.Length);
        var packet = CurrencyBoxUseRequestPacket.Read(new PacketReader(bytes));
        Assert.Equal(3103, packet.ItemTemplateId);
        Assert.Equal(quantity, packet.Quantity);
        Assert.Equal(id, packet.RequestId);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(3101, 0)]
    [InlineData(3101, -1)]
    [InlineData(3101, 1000000)]
    public void Request_RejectsInvalidItemOrQuantity(int itemId, int quantity)
    {
        Assert.Throws<PacketProtocolException>(() => CurrencyBoxUseRequestPacket.Read(
            new PacketReader(RequestBytes(itemId, quantity, Guid.NewGuid().ToString("D")))));
        Assert.Throws<InvalidOperationException>(() => new CurrencyBoxUseRequestPacket
        {
            ItemTemplateId = itemId,
            Quantity = quantity,
            RequestId = Guid.NewGuid()
        }.Serialize());
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx")]
    [InlineData("ab97fe3d887d4c01a51dd77e790986a1")]
    public void Request_RejectsInvalidRequestId(string text)
    {
        Assert.Throws<PacketProtocolException>(() => CurrencyBoxUseRequestPacket.Read(
            new PacketReader(RequestBytes(3101, 1, text))));
    }

    [Fact]
    public void Request_RejectsEveryTruncationTrailingBytesAndWrongOpcode()
    {
        var bytes = RequestBytes(3101, 1, Guid.NewGuid().ToString("D"));
        for (var size = PacketReader.HeaderSize; size < bytes.Length; size++)
        {
            var truncated = bytes[..size];
            BitConverter.GetBytes((ushort)size).CopyTo(truncated, 0);
            Assert.Throws<PacketProtocolException>(() => CurrencyBoxUseRequestPacket.Read(new PacketReader(truncated)));
        }

        var trailing = new byte[bytes.Length + 1];
        bytes.CopyTo(trailing, 0);
        BitConverter.GetBytes((ushort)trailing.Length).CopyTo(trailing, 0);
        Assert.Throws<PacketProtocolException>(() => CurrencyBoxUseRequestPacket.Read(new PacketReader(trailing)));

        BitConverter.GetBytes((ushort)Opcode.MailClaim).CopyTo(bytes, 2);
        Assert.Throws<PacketProtocolException>(() => CurrencyBoxUseRequestPacket.Read(new PacketReader(bytes)));
    }

    [Fact]
    public void Result_WritesSuccessSnapshotInContractOrder()
    {
        var id = Guid.NewGuid();
        var snapshot = Snapshot();
        var bytes = new CurrencyBoxUseResultPacket
        {
            RequestId = id,
            Status = CurrencyBoxUsePacketStatus.Used,
            Snapshot = snapshot
        }.Serialize();
        Assert.Equal(83, bytes.Length);
        var reader = new PacketReader(bytes);
        Assert.Equal(Opcode.CurrencyBoxUseResult, reader.Opcode);
        Assert.Equal((int)CurrencyBoxUsePacketStatus.Used, reader.ReadInt());
        Assert.Equal(id.ToString("D"), reader.ReadString());
        Assert.True(reader.ReadBool());
        Assert.Equal(snapshot.ItemTemplateId, reader.ReadInt());
        Assert.Equal(snapshot.UsedQuantity, reader.ReadInt());
        Assert.Equal(snapshot.QuantityAfter, reader.ReadInt());
        Assert.Equal((int)snapshot.CurrencyType, reader.ReadInt());
        Assert.Equal(snapshot.GrantedAmount, reader.ReadInt());
        Assert.Equal(snapshot.CurrentGold, reader.ReadInt());
        Assert.Equal(snapshot.CurrentGem, reader.ReadInt());
        Assert.Equal(new DateTimeOffset(snapshot.ProcessedAt).ToUnixTimeMilliseconds(), reader.ReadLong());
        reader.EnsureFullyRead();
    }

    [Fact]
    public void Result_AllFailuresContainOnlyStatusAndRequestId()
    {
        foreach (var status in Enum.GetValues<CurrencyBoxUsePacketStatus>().Where(status => status != CurrencyBoxUsePacketStatus.Used))
        {
            var id = Guid.NewGuid();
            var bytes = new CurrencyBoxUseResultPacket { RequestId = id, Status = status }.Serialize();
            Assert.Equal(47, bytes.Length);
            var reader = new PacketReader(bytes);
            Assert.Equal((int)status, reader.ReadInt());
            Assert.Equal(id.ToString("D"), reader.ReadString());
            Assert.False(reader.ReadBool());
            reader.EnsureFullyRead();
        }
    }

    [Fact]
    public void Result_RejectsInvalidStatusSnapshotAndRequestId()
    {
        var id = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => new CurrencyBoxUseResultPacket
        {
            RequestId = id,
            Status = CurrencyBoxUsePacketStatus.Used
        }.Serialize());
        Assert.Throws<InvalidOperationException>(() => new CurrencyBoxUseResultPacket
        {
            RequestId = id,
            Status = CurrencyBoxUsePacketStatus.InsufficientQuantity,
            Snapshot = Snapshot()
        }.Serialize());
        Assert.Throws<InvalidOperationException>(() => new CurrencyBoxUseResultPacket
        {
            RequestId = id,
            Status = (CurrencyBoxUsePacketStatus)999
        }.Serialize());
        Assert.Throws<InvalidOperationException>(() => new CurrencyBoxUseResultPacket
        {
            Status = CurrencyBoxUsePacketStatus.BoxNotFound
        }.Serialize());

        foreach (var snapshot in new[]
        {
            Snapshot() with { ItemTemplateId = 0 },
            Snapshot() with { UsedQuantity = 0 },
            Snapshot() with { QuantityAfter = 999999 },
            Snapshot() with { CurrencyType = (CurrencyType)999 },
            Snapshot() with { GrantedAmount = 0 },
            Snapshot() with { CurrentGold = 0 },
            Snapshot() with { CurrentGem = -1 },
            Snapshot() with { ProcessedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified) }
        })
        {
            Assert.Throws<InvalidOperationException>(() => new CurrencyBoxUseResultPacket
            {
                RequestId = id,
                Status = CurrencyBoxUsePacketStatus.Used,
                Snapshot = snapshot
            }.Serialize());
        }
    }

    private static CurrencyBoxUsePacketSnapshot Snapshot() =>
        new(3103, 2, 8, CurrencyType.Gold, 220, 1220, 500,
            new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc));

    private static byte[] RequestBytes(int itemId, int quantity, string requestId)
    {
        var body = new PacketWriter();
        body.WriteUShort((ushort)Opcode.CurrencyBoxUse);
        body.WriteInt(itemId);
        body.WriteInt(quantity);
        body.WriteString(requestId);
        var bytes = body.ToArray();
        var writer = new PacketWriter();
        writer.WriteUShort(checked((ushort)(bytes.Length + 2)));
        writer.WriteBytes(bytes);
        return writer.ToArray();
    }
}
