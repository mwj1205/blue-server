using blueServer.Game.Packets;
using blueServer.Game.Services;

namespace blueServer.Game.Handlers;

public sealed class PartySaveHandler : IPacketHandler
{
    private readonly PartyService _partyService;

    public PartySaveHandler(PartyService partyService)
    {
        _partyService = partyService;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playerId = session.PlayerId ??
            throw new InvalidOperationException("Party save handler requires authenticated session.");
        var request = PartySaveRequestPacket.Read(reader);

        var result = await _partyService.SaveAsync(
            playerId,
            request.PartyNo,
            request.Name,
            request.Slots
                .Select(slot => new PartySaveSlot(
                    slot.SlotIndex,
                    slot.OwnedCharacterId))
                .ToArray(),
            cancellationToken);

        await session.SendAsync(
            PartyPacketMapper.ToPacket(result).Serialize(),
            cancellationToken);
    }
}
