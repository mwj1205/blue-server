using blueServer.Game.Packets;
using blueServer.Infrastructure.Mails;

namespace blueServer.Game.Handlers;

public sealed class MailDetailHandler : IPacketHandler
{
    private readonly MailDetailQueryService _mailDetailQueryService;

    public MailDetailHandler(
        MailDetailQueryService mailDetailQueryService)
    {
        _mailDetailQueryService = mailDetailQueryService;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playerId = session.PlayerId ??
            throw new InvalidOperationException(
                "Mail detail handler requires authenticated session.");
        var request = MailDetailRequestPacket.Read(reader);

        // 다른 Player Mail의 존재 여부를 노출하지 않는 소유자 조건 조회
        var result = await _mailDetailQueryService.GetAsync(
            playerId,
            request.MailId,
            DateTime.UtcNow,
            cancellationToken);

        await session.SendAsync(
            MailPacketMapper.ToPacket(result).Serialize(),
            cancellationToken);
    }
}
