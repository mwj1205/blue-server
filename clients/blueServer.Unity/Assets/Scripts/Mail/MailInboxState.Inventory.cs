using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Models;
using BlueServer.Client.Network;

namespace BlueServer.Client.Mail
{
    public sealed partial class MailInboxState
    {
        public IReadOnlyList<InventoryListItem> InventoryItems { get; private set; } =
            new InventoryListItem[0];
        public bool CanQueryInventory { get { return _client is IInventoryQueryClient; } }
        public bool IsInventoryFresh { get; private set; }
        public bool IsInventoryLoading { get; private set; }
        public string InventoryError { get; private set; }

        public bool TryGetInventoryQuantity(int itemId, out int quantity)
        {
            quantity = 0;
            if (!IsInventoryFresh) return false;
            _knownQuantities.TryGetValue(itemId, out quantity);
            return true;
        }

        public async Task<bool> RefreshInventoryAsync(CancellationToken cancellationToken)
        {
            if (!CanQueryInventory) throw new InvalidOperationException("Inventory query is unavailable.");
            if (!TryBeginRequest()) return false;
            try
            {
                if (!await RefreshInventoryCoreAsync(cancellationToken))
                {
                    SetServerFailure(InventoryError);
                    return false;
                }
                CompleteRequest();
                return true;
            }
            catch (OperationCanceledException)
            {
                CancelRequest();
                throw;
            }
            catch (Exception exception)
            {
                SetExceptionFailure(exception);
                throw;
            }
        }

        private async Task<bool> RefreshInventoryCoreAsync(CancellationToken cancellationToken)
        {
            var client = _client as IInventoryQueryClient;
            if (client == null) return true;
            IsInventoryFresh = false;
            IsInventoryLoading = true;
            InventoryError = null;
            NotifyChanged();
            try
            {
                var response = await client.GetInventoryAsync(cancellationToken);
                if (!response.Success)
                {
                    InventoryError = "Inventory query failed: " + response.Status;
                    return false;
                }
                // 완성된 전체 목록으로 교체해 이전 목록에만 있던 아이템이 남지 않도록 한다.
                InventoryItems = response.Items;
                _knownQuantities.Clear();
                foreach (var item in response.Items)
                {
                    _knownQuantities.Add(item.ItemTemplateId, item.Quantity);
                }
                IsInventoryFresh = true;
                return true;
            }
            catch (Exception exception)
            {
                InventoryError = exception is OperationCanceledException
                    ? "Inventory query was cancelled. Refresh after reconnecting."
                    : exception.Message;
                throw;
            }
            finally
            {
                IsInventoryLoading = false;
                NotifyChanged();
            }
        }

        private void InvalidateInventory()
        {
            if (CanQueryInventory) IsInventoryFresh = false;
        }
    }
}
