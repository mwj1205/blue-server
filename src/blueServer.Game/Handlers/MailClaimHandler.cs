using blueServer.Game.Packets;
using blueServer.Infrastructure.Mails;

namespace blueServer.Game.Handlers;

public sealed class MailClaimHandler : IPacketHandler
{
    private readonly MailClaimService _mailClaimService;

    public MailClaimHandler(MailClaimService mailClaimService)
    {
        _mailClaimService = mailClaimService;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playerId = session.PlayerId ??
            throw new InvalidOperationException(
                "Mail claim handler requires authenticated session.");
        var request = MailClaimRequestPacket.Read(reader);
        var result = await _mailClaimService.ClaimAsync(
            playerId,
            request.MailId,
            DateTime.UtcNow,
            cancellationToken);

        await session.SendAsync(
            MailPacketMapper.ToPacket(result).Serialize(),
            cancellationToken);
    }
}
