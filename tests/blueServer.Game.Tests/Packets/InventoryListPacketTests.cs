using blueServer.Domain.Items;
using blueServer.Game.Packets;
using Xunit;

namespace blueServer.Game.Tests.Packets;

public sealed class InventoryListPacketTests
{
    [Fact]
    public void Request_RoundTripAndRejectsMalformedPayload()
    {
        var request = new InventoryListRequestPacket(Guid.NewGuid());
        var bytes = request.Serialize();
        Assert.Equal(42, bytes.Length);
        Assert.Equal(request, InventoryListRequestPacket.Read(new PacketReader(bytes)));
        Assert.Throws<InvalidOperationException>(() => new InventoryListRequestPacket(Guid.Empty).Serialize());

        foreach (var text in new[] { Guid.Empty.ToString("D"), new string('x', 36), Guid.NewGuid().ToString("N") })
        {
            var writer = new PacketWriter();
            writer.WriteUShort(checked((ushort)(6 + text.Length)));
            writer.WriteUShort((ushort)Opcode.InventoryList);
            writer.WriteString(text);
            Assert.Throws<PacketProtocolException>(() =>
                InventoryListRequestPacket.Read(new PacketReader(writer.ToArray())));
        }

        var wrongOpcode = (byte[])bytes.Clone();
        wrongOpcode[2] = (byte)Opcode.Ping;
        Assert.Throws<PacketProtocolException>(() =>
            InventoryListRequestPacket.Read(new PacketReader(wrongOpcode)));
        var truncated = bytes[..^1];
        BitConverter.GetBytes((ushort)truncated.Length).CopyTo(truncated, 0);
        Assert.Throws<PacketProtocolException>(() =>
            InventoryListRequestPacket.Read(new PacketReader(truncated)));
        var trailing = bytes.Concat(new byte[] { 0 }).ToArray();
        BitConverter.GetBytes((ushort)trailing.Length).CopyTo(trailing, 0);
        Assert.Throws<PacketProtocolException>(() =>
            InventoryListRequestPacket.Read(new PacketReader(trailing)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(310, 1)]
    [InlineData(311, 2)]
    [InlineData(10000, 33)]
    public void Result_FramesRestoreEntireOrderedInventoryWithinSizeLimit(int count, int expectedParts)
    {
        var items = Items(count);
        var requestId = Guid.NewGuid();
        var packet = new InventoryListResultPacket(requestId, InventoryListPacketStatus.Success, items);
        var frames = packet.SerializeFrames().ToArray();
        Assert.Equal(expectedParts, frames.Length);
        var restored = new List<InventoryListPacketItem>();
        for (var partIndex = 0; partIndex < frames.Length; partIndex++)
        {
            Assert.InRange(frames[partIndex].Length, 62, 4096);
            var reader = new PacketReader(frames[partIndex]);
            Assert.Equal(Opcode.InventoryListResult, reader.Opcode);
            Assert.Equal(requestId.ToString("D"), reader.ReadString());
            Assert.Equal((int)InventoryListPacketStatus.Success, reader.ReadInt());
            Assert.Equal(partIndex, reader.ReadInt());
            Assert.Equal(expectedParts, reader.ReadInt());
            Assert.Equal(count, reader.ReadInt());
            var itemCount = reader.ReadInt();
            Assert.InRange(itemCount, 0, 310);
            for (var index = 0; index < itemCount; index++)
            {
                restored.Add(new InventoryListPacketItem(
                    reader.ReadInt(), reader.ReadInt(), (ItemType)reader.ReadInt(), reader.ReadBool()));
            }
            reader.EnsureFullyRead();
        }
        Assert.Equal(items, restored);
    }

    [Theory]
    [InlineData(InventoryListPacketStatus.PlayerNotFound)]
    [InlineData(InventoryListPacketStatus.InventoryTooLarge)]
    public void Result_ErrorReturnsOneEmptyFrame(InventoryListPacketStatus status)
    {
        var frame = Assert.Single(new InventoryListResultPacket(Guid.NewGuid(), status, []).SerializeFrames());
        var reader = new PacketReader(frame);
        reader.ReadString();
        Assert.Equal((int)status, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        reader.EnsureFullyRead();
    }

    [Fact]
    public void Result_RejectsInvalidDataBeforeFirstFrame()
    {
        var valid = new InventoryListResultPacket(Guid.NewGuid(), InventoryListPacketStatus.Success, Items(311));
        var invalidTail = Items(311);
        invalidTail[^1] = invalidTail[^1] with { Quantity = 0 };
        foreach (var packet in new[]
        {
            valid with { RequestId = Guid.Empty },
            valid with { Status = (InventoryListPacketStatus)999 },
            valid with { Status = InventoryListPacketStatus.PlayerNotFound },
            valid with { Items = Items(10001) },
            valid with { Items = invalidTail },
            valid with { Items = [new(0, 1, ItemType.Material, true)] },
            valid with { Items = [new(1, 1000000, ItemType.Material, true)] },
            valid with { Items = [new(1, 1, (ItemType)999, true)] },
            valid with { Items = [new(1, 1, ItemType.Material, true), new(1, 2, ItemType.Material, true)] }
        })
        {
            using var frames = packet.SerializeFrames().GetEnumerator();
            Assert.Throws<InvalidOperationException>(() => frames.MoveNext());
        }
    }

    [Fact]
    public void Result_ObservesCancellationBetweenFrames()
    {
        using var cancellation = new CancellationTokenSource();
        var packet = new InventoryListResultPacket(Guid.NewGuid(), InventoryListPacketStatus.Success, Items(311));
        using var frames = packet.SerializeFrames(cancellation.Token).GetEnumerator();
        Assert.True(frames.MoveNext());
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => frames.MoveNext());
        Assert.Throws<OperationCanceledException>(() => packet.SerializeFrames(cancellation.Token).ToArray());
    }

    private static InventoryListPacketItem[] Items(int count) => Enumerable.Range(1, count)
        .Select(id => new InventoryListPacketItem(id, id, (ItemType)(id % 4 + 1), id % 2 == 0))
        .ToArray();
}
