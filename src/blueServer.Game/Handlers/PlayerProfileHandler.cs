using blueServer.Game.Packets;
using blueServer.Game.Services;

namespace blueServer.Game.Handlers;

public sealed class PlayerProfileHandler : IPacketHandler
{
    private readonly PlayerProfileService _playerProfileService;

    public PlayerProfileHandler(PlayerProfileService playerProfileService)
    {
        _playerProfileService = playerProfileService;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playerId = session.PlayerId ??
            throw new InvalidOperationException("Player profile handler requires authenticated session.");

        var result = await _playerProfileService.GetAsync(
            playerId,
            cancellationToken);

        var packet = new PlayerProfileResultPacket
        {
            Success = result.IsSuccess,
            Message = result.Message,
            PlayerId = result.PlayerId,
            Nickname = result.Nickname,
            Gold = result.Gold,
            Gem = result.Gem,
            OwnedCharacterCount = result.OwnedCharacterCount,
            PartyCount = result.PartyCount,
            ClearedStageCount = result.ClearedStageCount,
            TotalStageClearCount = result.TotalStageClearCount
        };

        await session.SendAsync(packet.Serialize(), cancellationToken);
    }
}
