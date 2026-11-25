using System;
using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Models;
using BlueServer.Client.Protocol;

namespace BlueServer.Client.Network
{
    public interface IInventoryQueryClient
    {
        Task<InventoryListResponse> GetInventoryAsync(CancellationToken cancellationToken);
    }

    public sealed partial class GameTcpClient : IInventoryQueryClient
    {
        public async Task<InventoryListResponse> GetInventoryAsync(CancellationToken cancellationToken)
        {
            var requestId = Guid.NewGuid();
            var assembler = new InventoryListAssembler(requestId);
            InventoryListResponse response = null;

            // 전체 수신 동안 기존 요청 잠금과 deadline을 유지한다.
            // 중간 실패는 ExchangeAsync에서 연결을 폐기하며 부분 결과를 반환하지 않는다.
            await ExchangeAsync(
                InventoryListPacketCodec.BuildRequest(requestId),
                cancellationToken,
                async (stream, token) =>
                {
                    while (!assembler.Append(await ReadPacketAsync(stream, token), out response))
                    {
                    }
                    return new byte[0];
                });

            return response;
        }
    }
}
