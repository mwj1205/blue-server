using blueServer.Game.Packets;
using blueServer.Infrastructure.Items;

namespace blueServer.Game.Handlers;

public sealed class InventoryListHandler : IPacketHandler
{
    private readonly InventoryListQueryService _inventoryListQueryService;

    public InventoryListHandler(InventoryListQueryService inventoryListQueryService)
    {
        _inventoryListQueryService = inventoryListQueryService;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playerId = session.PlayerId ??
            throw new InvalidOperationException(
                "Inventory list handler requires authenticated session.");
        var request = InventoryListRequestPacket.Read(reader);

        // 세션 소유자의 전체 목록을 한 번만 조회하고 같은 결과를 프레임으로 나눈다.
        var result = await _inventoryListQueryService.GetAsync(
            playerId,
            cancellationToken);
        var packet = InventoryPacketMapper.ToPacket(result, request.RequestId);
        foreach (var frame in packet.SerializeFrames(cancellationToken))
        {
            await session.SendAsync(frame, cancellationToken);
        }
    }
}
