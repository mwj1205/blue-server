using blueServer.Game.Packets;
using blueServer.Infrastructure.Mails;

namespace blueServer.Game.Handlers;

public sealed class MailListHandler : IPacketHandler
{
    private readonly MailListQueryService _mailListQueryService;

    public MailListHandler(MailListQueryService mailListQueryService)
    {
        _mailListQueryService = mailListQueryService;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playerId = session.PlayerId ??
            throw new InvalidOperationException(
                "Mail list handler requires authenticated session.");
        var request = MailListRequestPacket.Read(reader);

        // Client 입력 Player ID를 허용하지 않는 Session 소유자 기준 조회
        var result = await _mailListQueryService.GetAsync(
            playerId,
            DateTime.UtcNow,
            request.PageSize,
            request.Cursor,
            cancellationToken);

        await session.SendAsync(
            MailPacketMapper.ToPacket(result).Serialize(),
            cancellationToken);
    }
}
