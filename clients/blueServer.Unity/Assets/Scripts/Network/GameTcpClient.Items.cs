using System;
using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Models;
using BlueServer.Client.Protocol;

namespace BlueServer.Client.Network
{
    public interface IItemUseClient
    {
        Task<ItemMailClaimResponse> ClaimMailItemsAsync(long mailId, Guid requestId, CancellationToken ct);
        Task<CurrencyBoxUseResponse> UseCurrencyBoxAsync(int itemId, int quantity, Guid requestId, CancellationToken ct);
        Task<PlayerProfileResponse> GetPlayerProfileAsync(CancellationToken ct);
        Task<ItemMailClaimAllResponse> ClaimAllMailItemsAsync(Guid requestId, CancellationToken ct);
    }

    public sealed partial class GameTcpClient
    {
        public async Task<ItemMailClaimAllResponse> ClaimAllMailItemsAsync(Guid requestId, CancellationToken ct)
        {
            ItemMailClaimAllResponse result = null;
            using (var assembler = new MailClaimAllItemAssembler(requestId))
            {
                // 전 프레임 수신 동안 기존 request lock과 deadline을 유지한다.
                await ExchangeAsync(MailClaimAllItemCodec.BuildRequest(requestId), ct, async (stream, token) =>
                {
                    while (!assembler.Append(await ReadPacketAsync(stream, token), out result)) { }
                    return new byte[0];
                });
            }
            return result;
        }

        public async Task<ItemMailClaimResponse> ClaimMailItemsAsync(long mailId, Guid requestId, CancellationToken ct)
        {
            var request = ItemUsePacketCodec.BuildClaimRequest(mailId, requestId);
            try
            {
                return ItemUsePacketCodec.ParseClaim(await ExchangeAsync(request, ct), requestId);
            }
            catch (GameProtocolException)
            {
                Dispose();
                throw;
            }
        }

        public async Task<CurrencyBoxUseResponse> UseCurrencyBoxAsync(int itemId, int quantity, Guid requestId, CancellationToken ct)
        {
            var request = ItemUsePacketCodec.BuildUseRequest(itemId, quantity, requestId);
            try
            {
                return ItemUsePacketCodec.ParseUse(await ExchangeAsync(request, ct), requestId);
            }
            catch (GameProtocolException)
            {
                Dispose();
                throw;
            }
        }
    }
}
