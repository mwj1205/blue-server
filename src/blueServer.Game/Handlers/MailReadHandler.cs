using blueServer.Game.Packets;
using blueServer.Infrastructure.Mails;

namespace blueServer.Game.Handlers;

public sealed class MailReadHandler : IPacketHandler
{
    private readonly MailReadService _mailReadService;

    public MailReadHandler(MailReadService mailReadService)
    {
        _mailReadService = mailReadService;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playerId = session.PlayerId ??
            throw new InvalidOperationException(
                "Mail read handler requires authenticated session.");
        var request = MailReadRequestPacket.Read(reader);
        var result = await _mailReadService.MarkAsReadAsync(
            playerId,
            request.MailId,
            DateTime.UtcNow,
            cancellationToken);

        await session.SendAsync(
            MailPacketMapper.ToPacket(result).Serialize(),
            cancellationToken);
    }
}
