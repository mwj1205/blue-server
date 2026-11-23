using blueServer.Game.Packets;
using blueServer.Infrastructure.Items;

namespace blueServer.Game.Handlers;

public sealed class CurrencyBoxUseHandler : IPacketHandler
{
    private readonly CurrencyBoxUseService _currencyBoxUseService;

    public CurrencyBoxUseHandler(CurrencyBoxUseService currencyBoxUseService)
    {
        _currencyBoxUseService = currencyBoxUseService;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playerId = session.PlayerId ??
            throw new InvalidOperationException(
                "Currency box use handler requires authenticated session.");
        var request = CurrencyBoxUseRequestPacket.Read(reader);
        var result = await _currencyBoxUseService.UseAsync(
            playerId,
            new CurrencyBoxUseRequest(request.ItemTemplateId, request.Quantity, request.RequestId),
            DateTime.UtcNow,
            cancellationToken);

        await session.SendAsync(
            CurrencyBoxPacketMapper.ToPacket(result, request.RequestId).Serialize(),
            cancellationToken);
    }
}
