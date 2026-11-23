using System;
using System.Collections.Generic;

namespace BlueServer.Client.Models
{
    public sealed class MailItemAttachment
    {
        public int ItemTemplateId;
        public int Quantity;
        public int ClaimedQuantity;
        public int RemainingQuantity;
    }

    public sealed class ClaimedInventoryItem
    {
        public int ItemTemplateId;
        public int GrantedQuantity;
        public int RemainingQuantity;
        public int QuantityAfter;
    }

    public sealed class ItemMailClaimResponse
    {
        public Guid RequestId;
        public bool Success;
        public int Status;
        public string Message;
        public int CurrentGold;
        public int CurrentGem;
        public IReadOnlyList<ClaimedInventoryItem> Items;
    }

    public sealed class CurrencyBoxUseResponse
    {
        public Guid RequestId;
        public int Status;
        public bool Success { get { return Status == 0; } }
        public int ItemTemplateId;
        public int UsedQuantity;
        public int QuantityAfter;
        public int CurrencyType;
        public int GrantedAmount;
        public int CurrentGold;
        public int CurrentGem;
        public DateTime ProcessedAt;
    }

    public sealed class GrantedInventoryItem
    {
        public int ItemTemplateId;
        public int GrantedQuantity;
    }

    public sealed class ItemMailClaimAllResponse
    {
        public Guid RequestId;
        public int Status;
        public bool Success { get { return Status == 1 || Status == 5; } }
        public DateTime? ProcessedAt;
        public int ClaimedMailCount;
        public int PartiallyClaimedMailCount;
        public int NoCapacityMailCount;
        public int GrantedGold;
        public int GrantedGem;
        public int CurrentGold;
        public int CurrentGem;
        public bool HasMore;
        public IReadOnlyList<GrantedInventoryItem> Items;
    }
}
