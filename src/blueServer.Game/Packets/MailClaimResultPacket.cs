namespace blueServer.Game.Packets;

public sealed class MailClaimResultPacket
{
    public bool Success { get; init; }
    public MailClaimPacketStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime? ClaimedAt { get; init; }
    public int CurrentGold { get; init; }
    public int CurrentGem { get; init; }

    public byte[] Serialize()
    {
        var successfulStatus = Status is
            MailClaimPacketStatus.Claimed or
            MailClaimPacketStatus.AlreadyClaimed;

        if (Success != successfulStatus ||
            Success != ClaimedAt.HasValue)
        {
            throw new InvalidOperationException(
                "Mail claim success, status, and claimed time must agree.");
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
    IdempotencyConflict = 6
}
