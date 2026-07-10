using blueServer.Game.Packets;
using blueServer.Game.Services;

namespace blueServer.Game.Handlers;

internal static class PartyPacketMapper
{
    public static PartyResultPacket ToPacket(PartyResult result)
    {
        return new PartyResultPacket
        {
            Success = result.IsSuccess,
            Message = result.Message,
            PartyNo = result.PartyNo,
            Name = result.Name,
            Slots = result.Slots
                .Select(slot => new PartySlotPacketItem(
                    slot.SlotIndex,
                    slot.OwnedCharacterId,
                    slot.CharacterTemplateId,
                    slot.CharacterName,
                    slot.Rarity,
                    slot.Role,
                    slot.Level,
                    slot.Star,
                    slot.Exp))
                .ToArray()
        };
    }
}
