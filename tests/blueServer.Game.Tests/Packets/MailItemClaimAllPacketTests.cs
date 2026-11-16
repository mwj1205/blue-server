using blueServer.Game.Packets;
using Xunit;

namespace blueServer.Game.Tests.Packets;

public sealed class MailItemClaimAllPacketTests
{
    [Fact]
    public void Request_RoundTrip_RestoresRequestId()
    {
        var requestId = Guid.NewGuid();
        var packet = new MailClaimAllRequestPacket { RequestId = requestId }.Serialize();
        var reader = new PacketReader(packet);

        Assert.Equal(Opcode.MailClaimAll, reader.Opcode);
        Assert.Equal(requestId, MailClaimAllRequestPacket.Read(reader).RequestId);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx")]
    [InlineData("6c3b9494316a43e594ff3c0d4f456998")]
    public void Request_RejectsInvalidRequestId(string requestId)
    {
        var writer = new PacketWriter();
        writer.WriteUShort(checked((ushort)(PacketReader.HeaderSize + sizeof(ushort) + requestId.Length)));
        writer.WriteUShort((ushort)Opcode.MailClaimAll);
        writer.WriteString(requestId);

        Assert.Throws<PacketProtocolException>(() =>
            MailClaimAllRequestPacket.Read(new PacketReader(writer.ToArray())));
    }

    [Fact]
    public void Response_SplitsLargeResultWithoutLosingItemData()
    {
        var requestId = Guid.NewGuid();
        var processedAt = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        var items = Enumerable.Range(1, 8400)
            .Select(id => new MailClaimAllPacketItem(id, 2))
            .ToArray();
        var packet = new MailClaimAllRequestResultPacket(
            requestId,
            MailClaimAllPacketStatus.Processed,
            processedAt,
            ClaimedMailCount: 0,
            GrantedGold: 30,
            GrantedGem: 0,
            CurrentGold: 1030,
            CurrentGem: 500,
            HasMore: true,
            PartiallyClaimedMailCount: 1,
            NoCapacityMailCount: 2,
            items);

        var frames = packet.SerializeFrames().ToArray();

        Assert.True(frames.Length > 1);
        Assert.Equal(4096, frames[0].Length);
        using var payload = new MemoryStream();
        for (var index = 0; index < frames.Length; index++)
        {
            var frame = frames[index];
            Assert.InRange(frame.Length, 1, 4096);
            var reader = new PacketReader(frame);
            Assert.Equal(Opcode.MailClaimAllResult, reader.Opcode);
            Assert.Equal(requestId.ToString("D"), reader.ReadString());
            Assert.Equal(index, reader.ReadInt());
            Assert.Equal(frames.Length, reader.ReadInt());
            payload.Write(frame, frame.Length - reader.RemainingBytes, reader.RemainingBytes);
        }

        // 전체 본문은 ushort 프레임 길이를 넘더라도 손실 없이 복원된다.
        Assert.True(payload.Length > ushort.MaxValue);
        payload.Position = 0;
        using var body = new BinaryReader(payload);
        Assert.Equal((int)MailClaimAllPacketStatus.Processed, body.ReadInt32());
        Assert.True(body.ReadBoolean());
        Assert.Equal(new DateTimeOffset(processedAt).ToUnixTimeMilliseconds(), body.ReadInt64());
        Assert.Equal(0, body.ReadInt32());
        Assert.Equal(30, body.ReadInt32());
        Assert.Equal(0, body.ReadInt32());
        Assert.Equal(1030, body.ReadInt32());
        Assert.Equal(500, body.ReadInt32());
        Assert.True(body.ReadBoolean());
        Assert.Equal(1, body.ReadInt32());
        Assert.Equal(2, body.ReadInt32());
        Assert.Equal(items.Length, body.ReadInt32());
        foreach (var item in items)
        {
            Assert.Equal(item.ItemTemplateId, body.ReadInt32());
            Assert.Equal(item.GrantedQuantity, body.ReadInt32());
        }

        Assert.Equal(payload.Length, payload.Position);
    }

    [Fact]
    public void Response_UsesOneFrameForFailure()
    {
        var requestId = Guid.NewGuid();
        var packet = new MailClaimAllRequestResultPacket(
            requestId,
            MailClaimAllPacketStatus.ItemTemplateInactive,
            ProcessedAt: null,
            ClaimedMailCount: 0,
            GrantedGold: 0,
            GrantedGem: 0,
            CurrentGold: 0,
            CurrentGem: 0,
            HasMore: false,
            PartiallyClaimedMailCount: 0,
            NoCapacityMailCount: 0,
            []);

        var reader = new PacketReader(Assert.Single(packet.SerializeFrames()));

        Assert.Equal(requestId.ToString("D"), reader.ReadString());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal((int)MailClaimAllPacketStatus.ItemTemplateInactive, reader.ReadInt());
        Assert.False(reader.ReadBool());
    }

    [Fact]
    public void Response_SingleItemSummary_Uses104Bytes()
    {
        var packet = new MailClaimAllRequestResultPacket(
            Guid.NewGuid(), MailClaimAllPacketStatus.Processed, DateTime.UtcNow,
            1, 130, 0, 1130, 500, false, 1, 98,
            [new MailClaimAllPacketItem(1001, 999)]);

        Assert.Equal(104, Assert.Single(packet.SerializeFrames()).Length);
    }

    [Theory]
    [InlineData(500, 1)]
    [InlineData(501, 2)]
    public void Response_DistinctItemCount_DeterminesFrameBoundary(int itemCount, int frameCount)
    {
        var packet = new MailClaimAllRequestResultPacket(
            Guid.NewGuid(), MailClaimAllPacketStatus.Processed, DateTime.UtcNow,
            1, 0, 0, 1000, 500, false, 0, 0,
            Enumerable.Range(1, itemCount).Select(id => new MailClaimAllPacketItem(id, 1)).ToArray());

        var frames = packet.SerializeFrames().ToArray();

        Assert.Equal(frameCount, frames.Length);
        Assert.Equal(4096, frames[0].Length);
    }
}
