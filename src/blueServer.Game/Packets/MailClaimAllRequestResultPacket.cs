namespace blueServer.Game.Packets;

public sealed record MailClaimAllRequestResultPacket(
    Guid RequestId,
    MailClaimAllPacketStatus Status,
    DateTime? ProcessedAt,
    int ClaimedMailCount,
    int GrantedGold,
    int GrantedGem,
    int CurrentGold,
    int CurrentGem,
    bool HasMore,
    int PartiallyClaimedMailCount,
    int NoCapacityMailCount,
    IReadOnlyList<MailClaimAllPacketItem> Items)
{
    private const int EnvelopeSize = PacketReader.HeaderSize + 38 + 2 * sizeof(int);
    private const int MaxChunkSize = PacketReader.MaxPacketSize - EnvelopeSize;

    public IEnumerable<byte[]> SerializeFrames(CancellationToken cancellationToken = default)
    {
        if (RequestId == Guid.Empty)
        {
            throw new InvalidOperationException("Mail claim-all request id must not be empty.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var payload = SerializePayload(cancellationToken);
        var requestId = RequestId.ToString("D");
        var partCount = (payload.Length - 1) / MaxChunkSize + 1;

        // 각 프레임은 독립된 길이 헤더를 갖고, 본문 조각은 순번대로 합쳐서 해석한다.
        for (var partIndex = 0; partIndex < partCount; partIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = partIndex * MaxChunkSize;
            var chunkSize = Math.Min(MaxChunkSize, payload.Length - offset);
            var frame = new byte[EnvelopeSize + chunkSize];
            var writer = new PacketWriter();
            writer.WriteUShort(checked((ushort)frame.Length));
            writer.WriteUShort((ushort)Opcode.MailClaimAllResult);
            writer.WriteString(requestId);
            writer.WriteInt(partIndex);
            writer.WriteInt(partCount);
            writer.ToArray().CopyTo(frame, 0);
            payload.AsSpan(offset, chunkSize).CopyTo(frame.AsSpan(EnvelopeSize));
            yield return frame;
        }
    }

    private byte[] SerializePayload(CancellationToken cancellationToken)
    {
        var writer = new PacketWriter();
        writer.WriteInt((int)Status);
        WriteOptionalTime(writer, ProcessedAt);
        writer.WriteInt(ClaimedMailCount);
        writer.WriteInt(GrantedGold);
        writer.WriteInt(GrantedGem);
        writer.WriteInt(CurrentGold);
        writer.WriteInt(CurrentGem);
        writer.WriteBool(HasMore);
        writer.WriteInt(PartiallyClaimedMailCount);
        writer.WriteInt(NoCapacityMailCount);
        writer.WriteInt(Items.Count);

        foreach (var item in Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteInt(item.ItemTemplateId);
            writer.WriteInt(item.GrantedQuantity);
        }

        return writer.ToArray();
    }

    private static void WriteOptionalTime(PacketWriter writer, DateTime? time)
    {
        writer.WriteBool(time.HasValue);
        if (time.HasValue)
        {
            writer.WriteLong(MailPacketTime.ToUnixMilliseconds(time.Value));
        }
    }
}

public sealed record MailClaimAllPacketItem(int ItemTemplateId, int GrantedQuantity);
