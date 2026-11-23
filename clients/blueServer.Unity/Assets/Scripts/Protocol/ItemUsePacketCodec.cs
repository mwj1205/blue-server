using System;
using System.Collections.Generic;
using BlueServer.Client.Models;

namespace BlueServer.Client.Protocol
{
    public static class ItemUsePacketCodec
    {
        public static byte[] BuildClaimRequest(long mailId, Guid requestId)
        {
            if (mailId <= 0) throw new ArgumentOutOfRangeException(nameof(mailId));
            ValidateRequestId(requestId);
            using (var writer = new GamePacketWriter())
            {
                writer.WriteLong(mailId);
                writer.WriteString(requestId.ToString("D"));
                return writer.BuildPacket(GameOpcode.MailClaim);
            }
        }

        public static byte[] BuildUseRequest(int itemId, int quantity, Guid requestId)
        {
            if (itemId <= 0) throw new ArgumentOutOfRangeException(nameof(itemId));
            if (quantity < 1 || quantity > 999999) throw new ArgumentOutOfRangeException(nameof(quantity));
            ValidateRequestId(requestId);
            using (var writer = new GamePacketWriter())
            {
                writer.WriteInt(itemId);
                writer.WriteInt(quantity);
                writer.WriteString(requestId.ToString("D"));
                return writer.BuildPacket(GameOpcode.CurrencyBoxUse);
            }
        }

        public static IReadOnlyList<MailItemAttachment> ReadDetailItems(GamePacketReader reader)
        {
            var count = ReadCount(reader);
            var items = new List<MailItemAttachment>(count);
            var ids = new HashSet<int>();
            for (var i = 0; i < count; i++)
            {
                var item = new MailItemAttachment
                {
                    ItemTemplateId = reader.ReadInt(), Quantity = reader.ReadInt(),
                    ClaimedQuantity = reader.ReadInt(), RemainingQuantity = reader.ReadInt()
                };
                if (item.ItemTemplateId <= 0 || !ids.Add(item.ItemTemplateId) || item.Quantity <= 0 ||
                    item.ClaimedQuantity < 0 || item.RemainingQuantity < 0 ||
                    (long)item.ClaimedQuantity + item.RemainingQuantity != item.Quantity)
                    throw new GameProtocolException("Invalid Mail item quantities.");
                items.Add(item);
            }
            return items.AsReadOnly();
        }

        public static ItemMailClaimResponse ParseClaim(byte[] bytes, Guid expectedId)
        {
            var reader = Reader(bytes, GameOpcode.MailClaimResult);
            var result = new ItemMailClaimResponse { Success = reader.ReadBool(), Status = reader.ReadInt(), Message = reader.ReadString() };
            var claimedAt = OptionalTime(reader);
            result.CurrentGold = reader.ReadInt();
            result.CurrentGem = reader.ReadInt();
            result.RequestId = ReadId(reader, expectedId);
            var processedAt = OptionalTime(reader);
            var gold = reader.ReadInt();
            var gem = reader.ReadInt();
            var count = ReadCount(reader);
            var items = new List<ClaimedInventoryItem>(count);
            var ids = new HashSet<int>();
            for (var i = 0; i < count; i++)
            {
                var item = new ClaimedInventoryItem
                {
                    ItemTemplateId = reader.ReadInt(), GrantedQuantity = reader.ReadInt(),
                    RemainingQuantity = reader.ReadInt(), QuantityAfter = reader.ReadInt()
                };
                if (item.ItemTemplateId <= 0 || !ids.Add(item.ItemTemplateId) || item.GrantedQuantity < 0 ||
                    item.RemainingQuantity < 0 || item.QuantityAfter < item.GrantedQuantity || item.QuantityAfter > 999999)
                    throw new GameProtocolException("Invalid Mail claim item result.");
                items.Add(item);
            }
            var completed = result.Status == 0 || result.Status == 1;
            var success = completed || result.Status == 7 || result.Status == 8;
            if (result.Status < 0 || result.Status > 10 || result.Success != success ||
                claimedAt.HasValue != completed || (success && !processedAt.HasValue) ||
                result.CurrentGold < 0 || result.CurrentGem < 0 || gold < 0 || gem < 0)
                throw new GameProtocolException("Invalid Mail claim status or balance.");
            reader.EnsureFullyConsumed();
            result.Items = items.AsReadOnly();
            return result;
        }

        public static CurrencyBoxUseResponse ParseUse(byte[] bytes, Guid expectedId)
        {
            var reader = Reader(bytes, GameOpcode.CurrencyBoxUseResult);
            var result = new CurrencyBoxUseResponse { Status = reader.ReadInt() };
            result.RequestId = ReadId(reader, expectedId);
            var hasSnapshot = reader.ReadBool();
            if (result.Status < 0 || result.Status > 7 || result.Success != hasSnapshot)
                throw new GameProtocolException("Invalid box use status.");
            if (hasSnapshot)
            {
                result.ItemTemplateId = reader.ReadInt();
                result.UsedQuantity = reader.ReadInt();
                result.QuantityAfter = reader.ReadInt();
                result.CurrencyType = reader.ReadInt();
                result.GrantedAmount = reader.ReadInt();
                result.CurrentGold = reader.ReadInt();
                result.CurrentGem = reader.ReadInt();
                result.ProcessedAt = Time(reader);
                if (result.ItemTemplateId <= 0 || result.UsedQuantity <= 0 || result.QuantityAfter < 0 ||
                    (long)result.UsedQuantity + result.QuantityAfter > 999999 ||
                    (result.CurrencyType != 1 && result.CurrencyType != 2) || result.GrantedAmount <= 0 ||
                    result.CurrentGold < 0 || result.CurrentGem < 0 ||
                    (result.CurrencyType == 1 ? result.CurrentGold : result.CurrentGem) < result.GrantedAmount)
                    throw new GameProtocolException("Invalid box use snapshot.");
            }
            reader.EnsureFullyConsumed();
            return result;
        }

        private static int ReadCount(GamePacketReader reader)
        {
            var count = reader.ReadInt();
            if (count < 0 || count > reader.RemainingBytes / 16) throw new GameProtocolException("Invalid item count.");
            return count;
        }
        private static GamePacketReader Reader(byte[] bytes, GameOpcode opcode)
        {
            var reader = new GamePacketReader(bytes);
            if (reader.Opcode != opcode) throw new GameProtocolException("Unexpected item opcode.");
            return reader;
        }
        private static Guid ReadId(GamePacketReader reader, Guid expected)
        {
            Guid id;
            if (!Guid.TryParseExact(reader.ReadString(), "D", out id) || id == Guid.Empty || id != expected)
                throw new GameProtocolException("Response request id does not match.");
            return id;
        }
        private static DateTime? OptionalTime(GamePacketReader reader) { return reader.ReadBool() ? Time(reader) : (DateTime?)null; }
        private static DateTime Time(GamePacketReader reader)
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadLong()).UtcDateTime; }
            catch (ArgumentOutOfRangeException ex) { throw new GameProtocolException("Invalid timestamp.", ex); }
        }
        private static void ValidateRequestId(Guid id)
        {
            if (id == Guid.Empty) throw new ArgumentException("Request id is required.", nameof(id));
        }
    }
}
