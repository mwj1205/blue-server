using blueServer.Game.Packets;
using blueServer.Infrastructure.Mails;

namespace blueServer.Game.Handlers;

public sealed class MailClaimAllHandler : IPacketHandler
{
    private readonly MailClaimAllService _mailClaimAllService;

    public MailClaimAllHandler(MailClaimAllService mailClaimAllService)
    {
        _mailClaimAllService = mailClaimAllService;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playerId = session.PlayerId ??
            throw new InvalidOperationException(
                "Mail claim-all handler requires authenticated session.");
        var request = MailClaimAllRequestPacket.Read(reader);

        if (request.RequestId is { } requestId)
        {
            var requestResult = await _mailClaimAllService.ClaimAllAsync(
                playerId,
                new MailClaimAllRequest(requestId),
                DateTime.UtcNow,
                cancellationToken);

            var packet = MailPacketMapper.ToPacket(requestResult, requestId);
            foreach (var frame in packet.SerializeFrames(cancellationToken))
            {
                await session.SendAsync(frame, cancellationToken);
            }

            return;
        }

        var result = await _mailClaimAllService.ClaimAllAsync(
            playerId,
            DateTime.UtcNow,
            cancellationToken);

        await session.SendAsync(
            MailPacketMapper.ToPacket(result).Serialize(),
            cancellationToken);
    }
}
