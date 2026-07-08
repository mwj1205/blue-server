using blueServer.Game.Packets;
using blueServer.Game.Services;

namespace blueServer.Game.Handlers;

public sealed class OwnedCharacterListHandler : IPacketHandler
{
    private readonly OwnedCharacterListService _ownedCharacterListService;

    public OwnedCharacterListHandler(
        OwnedCharacterListService ownedCharacterListService)
    {
        _ownedCharacterListService = ownedCharacterListService;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playerId = session.PlayerId ??
            throw new InvalidOperationException("Owned character list handler requires authenticated session.");

        var result = await _ownedCharacterListService.GetAsync(
            playerId,
            cancellationToken);

        var packet = new OwnedCharacterListResultPacket
        {
            Success = result.IsSuccess,
            Message = result.Message,
            Characters = result.Characters
                .Select(character => new OwnedCharacterListPacketItem(
                    character.Id,
                    character.CharacterTemplateId,
                    character.CharacterName,
                    character.Rarity,
                    character.Role,
                    character.Level,
                    character.Star,
                    character.Exp))
                .ToArray()
        };

        await session.SendAsync(packet.Serialize(), cancellationToken);
    }
}
