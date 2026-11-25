using System;
using System.Collections.Generic;

namespace BlueServer.Client.Models
{
    public enum InventoryListStatus
    {
        Success = 0,
        PlayerNotFound = 1,
        InventoryTooLarge = 2
    }

    public enum InventoryItemType
    {
        Material = 1,
        CharacterShard = 2,
        Consumable = 3,
        EventCurrency = 4
    }

    public sealed class InventoryListItem
    {
        public int ItemTemplateId { get; private set; }
        public int Quantity { get; private set; }
        public InventoryItemType Type { get; private set; }
        public bool IsActive { get; private set; }

        public InventoryListItem(int itemTemplateId, int quantity, InventoryItemType type, bool isActive)
        {
            ItemTemplateId = itemTemplateId;
            Quantity = quantity;
            Type = type;
            IsActive = isActive;
        }
    }

    public sealed class InventoryListResponse
    {
        public Guid RequestId { get; private set; }
        public InventoryListStatus Status { get; private set; }
        public bool Success { get { return Status == InventoryListStatus.Success; } }
        public IReadOnlyList<InventoryListItem> Items { get; private set; }

        public InventoryListResponse(Guid requestId, InventoryListStatus status, IReadOnlyList<InventoryListItem> items)
        {
            RequestId = requestId;
            Status = status;
            Items = items;
        }
    }
}
