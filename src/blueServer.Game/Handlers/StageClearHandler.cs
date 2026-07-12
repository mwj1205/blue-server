using blueServer.Game.Packets;
using blueServer.Game.Services;

namespace blueServer.Game.Handlers;

public sealed class StageClearHandler : IPacketHandler
{
    private readonly StageClearService _stageClearService;

    public StageClearHandler(StageClearService stageClearService)
    {
        _stageClearService = stageClearService;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playerId = session.PlayerId ??
            throw new InvalidOperationException("Stage clear handler requires authenticated session.");
        var request = StageClearRequestPacket.Read(reader);

        var result = await _stageClearService.ClearAsync(
            playerId,
            request.StageTemplateId,
            request.PartyNo,
            cancellationToken);

        await session.SendAsync(
            ToPacket(result).Serialize(),
            cancellationToken);
    }

    private static StageClearResultPacket ToPacket(StageClearResult result)
    {
        return new StageClearResultPacket
        {
            Success = result.IsSuccess,
            Message = result.Message,
            StageTemplateId = result.StageTemplateId,
            StageName = result.StageName,
            PartyNo = result.PartyNo,
            RewardGold = result.RewardGold,
            RewardGem = result.RewardGem,
            CurrentGold = result.CurrentGold,
            CurrentGem = result.CurrentGem,
            ClearCount = result.ClearCount
        };
    }
}
