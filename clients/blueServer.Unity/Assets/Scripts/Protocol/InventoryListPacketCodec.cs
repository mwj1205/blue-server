using System;
using System.Collections.Generic;
using BlueServer.Client.Models;

namespace BlueServer.Client.Protocol
{
    public static class InventoryListPacketCodec
    {
        public static byte[] BuildRequest(Guid requestId)
        {
            if (requestId == Guid.Empty)
            {
                throw new ArgumentException("Request id is required.", nameof(requestId));
            }

            using (var writer = new GamePacketWriter())
            {
                writer.WriteString(requestId.ToString("D"));
                return writer.BuildPacket(GameOpcode.InventoryList);
            }
        }
    }

    public sealed class InventoryListAssembler
    {
        private const int MaxItems = 10000;
        private const int ItemsPerFrame = 310;
        private readonly Guid _requestId;
        private readonly List<InventoryListItem> _items = new List<InventoryListItem>();
        private int _nextPart;
        private int _totalItems;
        private InventoryListStatus _status;
        private bool _finished;

        public InventoryListAssembler(Guid requestId)
        {
            if (requestId == Guid.Empty)
            {
                throw new ArgumentException("Request id is required.", nameof(requestId));
            }
            _requestId = requestId;
        }

        public bool Append(byte[] frame, out InventoryListResponse response)
        {
            response = null;
            if (_finished)
            {
                throw new GameProtocolException("Inventory response is already completed or invalid.");
            }
            try
            {
                return AppendCore(frame, out response);
            }
            catch
            {
                _finished = true;
                _items.Clear();
                throw;
            }
        }

        private bool AppendCore(byte[] frame, out InventoryListResponse response)
        {
            response = null;
            if (frame == null || frame.Length < 62 || frame.Length > GamePacketWriter.MaxPacketSize)
            {
                throw new GameProtocolException("Invalid inventory frame size.");
            }
            var reader = new GamePacketReader(frame);
            Guid requestId;
            if (reader.Opcode != GameOpcode.InventoryListResult ||
                !Guid.TryParseExact(reader.ReadString(), "D", out requestId) || requestId != _requestId)
            {
                throw new GameProtocolException("Inventory opcode or request id does not match.");
            }
            var status = reader.ReadInt();
            var partIndex = reader.ReadInt();
            var partCount = reader.ReadInt();
            var totalItems = reader.ReadInt();
            var count = reader.ReadInt();
            if (status < 0 || status > 2 || partIndex != _nextPart ||
                totalItems < 0 || totalItems > MaxItems ||
                partCount != Math.Max(1, (totalItems + ItemsPerFrame - 1) / ItemsPerFrame) ||
                partIndex >= partCount || (status != 0 && totalItems != 0))
            {
                throw new GameProtocolException("Invalid inventory response envelope.");
            }
            if (_nextPart == 0)
            {
                _totalItems = totalItems;
                _status = (InventoryListStatus)status;
            }
            if (totalItems != _totalItems || status != (int)_status ||
                count != Math.Min(ItemsPerFrame, totalItems - _items.Count) ||
                count < 0 || reader.RemainingBytes != count * 13)
            {
                throw new GameProtocolException("Inconsistent inventory response counts or status.");
            }
            for (var index = 0; index < count; index++)
            {
                var id = reader.ReadInt();
                var quantity = reader.ReadInt();
                var type = reader.ReadInt();
                var active = reader.ReadBool();
                var previousId = _items.Count == 0 ? 0 : _items[_items.Count - 1].ItemTemplateId;
                if (id <= previousId || quantity < 1 || quantity > 999999 || type < 1 || type > 4)
                {
                    throw new GameProtocolException("Invalid inventory item or ordering.");
                }
                _items.Add(new InventoryListItem(id, quantity, (InventoryItemType)type, active));
            }
            reader.EnsureFullyConsumed();
            _nextPart++;
            if (_nextPart != partCount)
            {
                return false;
            }
            _finished = true;
            response = new InventoryListResponse(_requestId, _status, _items.AsReadOnly());
            return true;
        }
    }
}
