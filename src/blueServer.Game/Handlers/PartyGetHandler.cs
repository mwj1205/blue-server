using blueServer.Game.Packets;
using blueServer.Game.Services;

namespace blueServer.Game.Handlers;

public sealed class PartyGetHandler : IPacketHandler
{
    private readonly PartyService _partyService;

    public PartyGetHandler(PartyService partyService)
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
            throw new InvalidOperationException("Party get handler requires authenticated session.");
        var request = PartyGetRequestPacket.Read(reader);

        var result = await _partyService.GetAsync(
            playerId,
            request.PartyNo,
            cancellationToken);

        await session.SendAsync(
            PartyPacketMapper.ToPacket(result).Serialize(),
            cancellationToken);
    }
}
