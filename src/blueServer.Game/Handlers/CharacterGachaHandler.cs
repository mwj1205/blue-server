using blueServer.Game.Packets;
using blueServer.Game.Services;

namespace blueServer.Game.Handlers;

public sealed class CharacterGachaHandler : IPacketHandler
{
    private readonly CharacterGachaService _gachaService;

    public CharacterGachaHandler(CharacterGachaService gachaService)
    {
        _gachaService = gachaService;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (session.PlayerId is not long playerId)
        {
            await SendResultAsync(
                session,
                CharacterGachaResult.Fail("Login required"),
                cancellationToken);
            return;
        }

        var result = await _gachaService.DrawAsync(playerId, cancellationToken);
        await SendResultAsync(session, result, cancellationToken);
    }

    private static Task SendResultAsync(
        Session session,
        CharacterGachaResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var packet = new CharacterGachaResultPacket
        {
            Success = result.IsSuccess,
            Message = result.Message,
            OwnedCharacterId = result.OwnedCharacterId,
            CharacterTemplateId = result.CharacterTemplateId,
            CharacterName = result.CharacterName,
            Rarity = result.Rarity,
            RemainingGem = result.RemainingGem
        };

        return session.SendAsync(packet.Serialize());
    }
}
