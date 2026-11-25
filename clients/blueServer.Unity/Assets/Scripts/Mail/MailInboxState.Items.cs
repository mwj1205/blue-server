using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Models;
using BlueServer.Client.Network;
using BlueServer.Client.Protocol;

namespace BlueServer.Client.Mail
{
    public sealed partial class MailInboxState
    {
        private readonly Dictionary<long, Guid> _claimRequests = new Dictionary<long, Guid>();
        private readonly Dictionary<int, int> _knownQuantities = new Dictionary<int, int>();
        private Guid _useRequestId;
        private int _useItemId;
        private int _useQuantity;
        private bool _useUncertain;
        private Guid _claimAllRequestId;
        private bool _claimAllUncertain;

        public string ItemMessage { get; private set; }
        public bool CanUseItems { get { return _client is IItemUseClient; } }
        public bool CanRetryUse { get { return _useRequestId != Guid.Empty; } }
        public bool CanRetryClaimAll { get { return _claimAllRequestId != Guid.Empty; } }
        public ItemMailClaimAllResponse LastItemClaimAllResult { get; private set; }
        public IReadOnlyDictionary<int, int> KnownQuantities { get { return _knownQuantities; } }

        public async Task<bool> ClaimAllItemsAsync(bool retry, CancellationToken ct)
        {
            var client = _client as IItemUseClient;
            if (client == null) throw new InvalidOperationException("Item claim-all is unavailable.");
            if (IsBusy) return false;
            if (retry)
            {
                if (!CanRetryClaimAll) return false;
            }
            else
            {
                if (_claimAllUncertain)
                {
                    ItemMessage = "Previous Claim All is unresolved. Retry the same request first.";
                    return false;
                }
                _claimAllRequestId = Guid.NewGuid();
            }
            if (!TryBeginRequest()) return false;
            var wasUncertain = _claimAllUncertain;
            try
            {
                _claimAllUncertain = true;
                InvalidateInventory();
                ItemMessage = "Claim All request=" + _claimAllRequestId.ToString("D");
                var result = await client.ClaimAllMailItemsAsync(_claimAllRequestId, ct);
                LastItemClaimAllResult = result;
                _claimAllUncertain = result.Status == 3;
                ItemMessage = string.Format(
                    "Claim All: completed {0}, partial {1}, no capacity {2}; Gold +{3}, Gem +{4}\nRequestId={5}{6}",
                    result.ClaimedMailCount, result.PartiallyClaimedMailCount, result.NoCapacityMailCount,
                    result.GrantedGold, result.GrantedGem, result.RequestId.ToString("D"), retry ? " (original result)" : "");
                if (!result.Success)
                {
                    SetServerFailure("Claim All failed. Status=" + result.Status);
                    return false;
                }
                if (result.Status == 1) ItemMessage += "\nNo claimable Mail.";
                for (var i = 0; i < result.Items.Count; i++)
                {
                    var item = result.Items[i];
                    // 집계 응답에 현재 보유량이 없으므로 오래된 값을 증가시키거나 현재 값처럼 남기지 않는다.
                    if (!retry || wasUncertain) _knownQuantities.Remove(item.ItemTemplateId);
                    if (i < 8) ItemMessage += string.Format("\nItem {0}: received +{1:N0}", item.ItemTemplateId, item.GrantedQuantity);
                }
                if (result.Items.Count > 8) ItemMessage += "\nAdditional item types: " + (result.Items.Count - 8);
                if (result.HasMore) ItemMessage += "\nMore candidates exist. Free capacity if needed, then start a NEW Claim All.";
                var profile = await client.GetPlayerProfileAsync(ct);
                if (!profile.Success) throw new GameProtocolException(profile.Message);
                SetCurrentBalance(profile.Gold, profile.Gem);
                if (!await RefreshInventoryCoreAsync(ct))
                {
                    SetServerFailure("Claim All confirmed; " + InventoryError);
                    return false;
                }
                RequiresRefresh = true;
                CompleteRequest();
                // 자동 반복하지 않는다. 공간 부족 Mail이 계속 후보가 될 수 있다.
                return await LoadFirstPageAsync(20, ct);
            }
            catch (OperationCanceledException)
            {
                MarkMutationUncertain(); CancelRequest(); throw;
            }
            catch (Exception ex)
            {
                MarkMutationUncertain(); SetExceptionFailure(ex); throw;
            }
        }

        private async Task<bool> ClaimItemsAsync(long mailId, CancellationToken ct)
        {
            ValidateMailId(mailId);
            if (!TryBeginRequest()) return false;
            Guid id;
            if (!_claimRequests.TryGetValue(mailId, out id))
            {
                id = Guid.NewGuid();
                _claimRequests.Add(mailId, id);
            }
            try
            {
                ItemMessage = "Claim request=" + id.ToString("D");
                InvalidateInventory();
                var result = await ((IItemUseClient)_client).ClaimMailItemsAsync(mailId, id, ct);
                ItemMessage = "Claim: " + result.Message + " / RequestId=" + id.ToString("D");
                if (!result.Success)
                {
                    RequiresRefresh = true;
                    SetServerFailure(result.Message);
                    return false;
                }
                _claimRequests.Remove(mailId);
                SetCurrentBalance(result.CurrentGold, result.CurrentGem);
                foreach (var item in result.Items)
                {
                    _knownQuantities[item.ItemTemplateId] = item.QuantityAfter;
                    ItemMessage += string.Format("\nItem {0}: granted {1}, remaining Mail {2}, inventory {3}",
                        item.ItemTemplateId, item.GrantedQuantity, item.RemainingQuantity, item.QuantityAfter);
                }
                // 부분 수령 여부와 만료 시각은 서버에서 다시 읽는다. 클라이언트에서 완료로 추정하지 않는다.
                if (!await RefreshInventoryCoreAsync(ct))
                {
                    SetServerFailure("Claim confirmed; " + InventoryError);
                    return false;
                }
                RequiresRefresh = true;
                CompleteRequest();
                return await LoadDetailAsync(mailId, ct);
            }
            catch (OperationCanceledException)
            {
                MarkMutationUncertain(); CancelRequest(); throw;
            }
            catch (Exception ex)
            {
                MarkMutationUncertain(); SetExceptionFailure(ex); throw;
            }
        }

        public async Task<bool> UseBoxAsync(int itemId, int quantity, bool retry, CancellationToken ct)
        {
            var client = _client as IItemUseClient;
            if (client == null) throw new InvalidOperationException("Item use is unavailable.");
            if (IsBusy) return false;
            if (retry)
            {
                if (!CanRetryUse) return false;
                itemId = _useItemId;
                quantity = _useQuantity;
            }
            else
            {
                if (_useUncertain)
                {
                    ItemMessage = "Previous use is unresolved. Retry the same request before starting a new use.";
                    return false;
                }
                // 송신 전에 검사하고, 작업마다 한 번만 RequestId를 생성한다.
                var id = Guid.NewGuid();
                ItemUsePacketCodec.BuildUseRequest(itemId, quantity, id);
                _useItemId = itemId;
                _useQuantity = quantity;
                _useRequestId = id;
            }
            if (!TryBeginRequest()) return false;
            var wasUncertain = _useUncertain;
            try
            {
                _useUncertain = true;
                InvalidateInventory();
                ItemMessage = "Box request=" + _useRequestId.ToString("D");
                var result = await client.UseCurrencyBoxAsync(itemId, quantity, _useRequestId, ct);
                _useUncertain = result.Status == 7;
                ItemMessage = "Box status=" + result.Status + " / RequestId=" + result.RequestId.ToString("D");
                if (!result.Success)
                {
                    SetServerFailure(ItemMessage);
                    return false;
                }
                if (!retry || wasUncertain) _knownQuantities[itemId] = result.QuantityAfter;
                ItemMessage += string.Format("\n{0} +{1}, used {2}, inventory after use {3}{4}",
                    result.CurrencyType == 1 ? "Gold" : "Gem", result.GrantedAmount,
                    result.UsedQuantity, result.QuantityAfter, retry ? " (original snapshot)" : "");
                // 재전송 응답은 과거 snapshot일 수 있으므로 현재 잔액은 별도 조회한다.
                var profile = await client.GetPlayerProfileAsync(ct);
                if (!profile.Success) throw new GameProtocolException(profile.Message);
                SetCurrentBalance(profile.Gold, profile.Gem);
                if (!await RefreshInventoryCoreAsync(ct))
                {
                    SetServerFailure("Box use confirmed; " + InventoryError);
                    return false;
                }
                RequiresRefresh = true;
                CompleteRequest();
                return true;
            }
            catch (OperationCanceledException)
            {
                MarkMutationUncertain(); CancelRequest(); throw;
            }
            catch (Exception ex)
            {
                MarkMutationUncertain(); SetExceptionFailure(ex); throw;
            }
        }
    }
}
