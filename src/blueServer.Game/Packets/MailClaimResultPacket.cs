namespace blueServer.Game.Packets;

public sealed class MailClaimResultPacket
{
    public bool Success { get; init; }
    public MailClaimPacketStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime? ClaimedAt { get; init; }
    public int CurrentGold { get; init; }
    public int CurrentGem { get; init; }
    public Guid? RequestId { get; init; }
    public DateTime? ProcessedAt { get; init; }
    public int GrantedGold { get; init; }
    public int GrantedGem { get; init; }
    public IReadOnlyList<MailClaimPacketItem> Items { get; init; } =
        Array.Empty<MailClaimPacketItem>();

    public byte[] Serialize()
    {
        var completed = Status is
            MailClaimPacketStatus.Claimed or
            MailClaimPacketStatus.AlreadyClaimed;
        var successfulStatus = completed || (RequestId.HasValue &&
            Status is MailClaimPacketStatus.PartiallyClaimed or MailClaimPacketStatus.NoCapacity);

        if (Success != successfulStatus ||
            completed != ClaimedAt.HasValue)
        {
            throw new InvalidOperationException(
                "Mail claim success, status, and claimed time must agree.");
        }

        if (!Enum.IsDefined(Status) || RequestId == Guid.Empty ||
            (!RequestId.HasValue && (int)Status > (int)MailClaimPacketStatus.IdempotencyConflict) ||
            (!RequestId.HasValue && (ProcessedAt.HasValue || GrantedGold != 0 || GrantedGem != 0 || Items.Count != 0)) ||
            GrantedGold < 0 || GrantedGem < 0 ||
            Items.Any(item => item.ItemTemplateId <= 0 || item.GrantedQuantity < 0 ||
                item.RemainingQuantity < 0 || item.QuantityAfter < 0))
        {
            throw new InvalidOperationException(
                "Invalid Mail claim extension fields.");
        }

        if (CurrentGold < 0 || CurrentGem < 0)
        {
            throw new InvalidOperationException(
                "Mail claim balances cannot be negative.");
        }

        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.MailClaimResult);
        bodyWriter.WriteBool(Success);
        bodyWriter.WriteInt((int)Status);
        bodyWriter.WriteString(Message);
        bodyWriter.WriteBool(ClaimedAt.HasValue);

        if (ClaimedAt.HasValue)
        {
            bodyWriter.WriteLong(
                MailPacketTime.ToUnixMilliseconds(ClaimedAt.Value));
        }

        bodyWriter.WriteInt(CurrentGold);
        bodyWriter.WriteInt(CurrentGem);

        if (RequestId.HasValue)
        {
            bodyWriter.WriteString(RequestId.Value.ToString("D"));
            bodyWriter.WriteBool(ProcessedAt.HasValue);

            if (ProcessedAt.HasValue)
            {
                bodyWriter.WriteLong(
                    MailPacketTime.ToUnixMilliseconds(ProcessedAt.Value));
            }

            bodyWriter.WriteInt(GrantedGold);
            bodyWriter.WriteInt(GrantedGem);
            bodyWriter.WriteInt(Items.Count);

            foreach (var item in Items)
            {
                bodyWriter.WriteInt(item.ItemTemplateId);
                bodyWriter.WriteInt(item.GrantedQuantity);
                bodyWriter.WriteInt(item.RemainingQuantity);
                bodyWriter.WriteInt(item.QuantityAfter);
            }
        }

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort(checked((ushort)(body.Length + 2)));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}

public enum MailClaimPacketStatus
{
    Claimed = 0,
    AlreadyClaimed = 1,
    NotFound = 2,
    Expired = 3,
    NoRewards = 4,
    ConcurrencyConflict = 5,
    IdempotencyConflict = 6,
    PartiallyClaimed = 7,
    NoCapacity = 8,
    ItemTemplateNotFound = 9,
    ItemTemplateInactive = 10
}

public sealed record MailClaimPacketItem(
    int ItemTemplateId,
    int GrantedQuantity,
    int RemainingQuantity,
    int QuantityAfter);
