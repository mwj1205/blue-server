using blueServer.Game.Packets;
using blueServer.Infrastructure.Items;

namespace blueServer.Game.Handlers;

public static class CurrencyBoxPacketMapper
{
    public static CurrencyBoxUseResultPacket ToPacket(
        CurrencyBoxUseResult result,
        Guid requestId)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id must not be empty.", nameof(requestId));
        }

        var status = result.Status switch
        {
            CurrencyBoxUseStatus.Used => CurrencyBoxUsePacketStatus.Used,
            CurrencyBoxUseStatus.PlayerNotFound => CurrencyBoxUsePacketStatus.PlayerNotFound,
            CurrencyBoxUseStatus.BoxNotFound => CurrencyBoxUsePacketStatus.BoxNotFound,
            CurrencyBoxUseStatus.InsufficientQuantity => CurrencyBoxUsePacketStatus.InsufficientQuantity,
            CurrencyBoxUseStatus.ItemTemplateNotFound => CurrencyBoxUsePacketStatus.ItemTemplateNotFound,
            CurrencyBoxUseStatus.ItemTemplateInactive => CurrencyBoxUsePacketStatus.ItemTemplateInactive,
            CurrencyBoxUseStatus.IdempotencyConflict => CurrencyBoxUsePacketStatus.IdempotencyConflict,
            CurrencyBoxUseStatus.ConcurrencyConflict => CurrencyBoxUsePacketStatus.ConcurrencyConflict,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result.Status), result.Status, "Unexpected currency box use status.")
        };

        if ((result.Status == CurrencyBoxUseStatus.Used) != (result.Snapshot is not null) ||
            (result.Snapshot is { } value && value.RequestId != requestId))
        {
            throw new InvalidOperationException("Currency box use result does not match its status or request id.");
        }

        return new CurrencyBoxUseResultPacket
        {
            RequestId = requestId,
            Status = status,
            Snapshot = result.Snapshot is { } snapshot
                ? new CurrencyBoxUsePacketSnapshot(
                    snapshot.ItemTemplateId,
                    snapshot.UsedQuantity,
                    snapshot.QuantityAfter,
                    snapshot.CurrencyType,
                    snapshot.GrantedAmount,
                    snapshot.CurrentGold,
                    snapshot.CurrentGem,
                    snapshot.ProcessedAt)
                : null
        };
    }
}
