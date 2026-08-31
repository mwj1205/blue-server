namespace blueServer.Game.Packets;

public sealed class MailClaimAllResultPacket
{
    public bool Success { get; init; }
    public MailClaimAllPacketStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public int ClaimedMailCount { get; init; }
    public int GrantedGold { get; init; }
    public int GrantedGem { get; init; }
    public int CurrentGold { get; init; }
    public int CurrentGem { get; init; }
    public bool HasMore { get; init; }

    public byte[] Serialize()
    {
        Validate();

        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.MailClaimAllResult);
        bodyWriter.WriteBool(Success);
        bodyWriter.WriteInt((int)Status);
        bodyWriter.WriteString(Message);
        bodyWriter.WriteInt(ClaimedMailCount);
        bodyWriter.WriteInt(GrantedGold);
        bodyWriter.WriteInt(GrantedGem);
        bodyWriter.WriteInt(CurrentGold);
        bodyWriter.WriteInt(CurrentGem);
        bodyWriter.WriteBool(HasMore);

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort(checked((ushort)(body.Length + 2)));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }

    private void Validate()
    {
        var successfulStatus = Status is
            MailClaimAllPacketStatus.Claimed or
            MailClaimAllPacketStatus.NothingToClaim;

        if (Success != successfulStatus)
        {
            throw new InvalidOperationException(
                "Mail claim-all success and status must agree.");
        }

        if (ClaimedMailCount < 0 ||
            GrantedGold < 0 ||
            GrantedGem < 0 ||
            CurrentGold < 0 ||
            CurrentGem < 0)
        {
            throw new InvalidOperationException(
                "Mail claim-all counts and balances cannot be negative.");
        }

        if (Status == MailClaimAllPacketStatus.Claimed &&
            ClaimedMailCount == 0)
        {
            throw new InvalidOperationException(
                "Claimed result must contain at least one claimed Mail.");
        }

        if (Status == MailClaimAllPacketStatus.NothingToClaim &&
            (ClaimedMailCount != 0 ||
                GrantedGold != 0 ||
                GrantedGem != 0 ||
                HasMore))
        {
            throw new InvalidOperationException(
                "Nothing-to-claim result cannot contain granted rewards or more Mail.");
        }

        if (!Success &&
            (ClaimedMailCount != 0 ||
                GrantedGold != 0 ||
                GrantedGem != 0 ||
                CurrentGold != 0 ||
                CurrentGem != 0 ||
                HasMore))
        {
            throw new InvalidOperationException(
                "Failed claim-all result cannot contain reward or balance data.");
        }
    }
}

public enum MailClaimAllPacketStatus
{
    Claimed = 0,
    NothingToClaim = 1,
    PlayerNotFound = 2,
    ConcurrencyConflict = 3,
    IdempotencyConflict = 4
}
