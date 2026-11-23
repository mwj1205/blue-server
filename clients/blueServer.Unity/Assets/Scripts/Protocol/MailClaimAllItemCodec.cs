using System;
using System.Collections.Generic;
using System.IO;
using BlueServer.Client.Models;

namespace BlueServer.Client.Protocol
{
    public static class MailClaimAllItemCodec
    {
        public static byte[] BuildRequest(Guid requestId)
        {
            if (requestId == Guid.Empty) throw new ArgumentException("Request id is required.", nameof(requestId));
            using (var writer = new GamePacketWriter())
            {
                writer.WriteString(requestId.ToString("D"));
                return writer.BuildPacket(GameOpcode.MailClaimAll);
            }
        }

        internal static ItemMailClaimAllResponse ParsePayload(byte[] payload, Guid requestId)
        {
            var reader = new GamePacketReader(payload, GameOpcode.MailClaimAllResult);
            var result = new ItemMailClaimAllResponse { RequestId = requestId, Status = reader.ReadInt() };
            if (reader.ReadBool())
            {
                try { result.ProcessedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadLong()).UtcDateTime; }
                catch (ArgumentOutOfRangeException ex) { throw new GameProtocolException("Invalid claim-all time.", ex); }
            }
            result.ClaimedMailCount = reader.ReadInt();
            result.GrantedGold = reader.ReadInt();
            result.GrantedGem = reader.ReadInt();
            result.CurrentGold = reader.ReadInt();
            result.CurrentGem = reader.ReadInt();
            result.HasMore = reader.ReadBool();
            result.PartiallyClaimedMailCount = reader.ReadInt();
            result.NoCapacityMailCount = reader.ReadInt();
            var count = reader.ReadInt();
            if (count < 0 || count > reader.RemainingBytes / 8)
                throw new GameProtocolException("Invalid claim-all item count.");
            var items = new List<GrantedInventoryItem>(count);
            var ids = new HashSet<int>();
            for (var i = 0; i < count; i++)
            {
                var item = new GrantedInventoryItem { ItemTemplateId = reader.ReadInt(), GrantedQuantity = reader.ReadInt() };
                if (item.ItemTemplateId <= 0 || item.GrantedQuantity <= 0 || !ids.Add(item.ItemTemplateId))
                    throw new GameProtocolException("Invalid claim-all item summary.");
                items.Add(item);
            }
            reader.EnsureFullyConsumed();
            var processed = (long)result.ClaimedMailCount + result.PartiallyClaimedMailCount + result.NoCapacityMailCount;
            if (result.Status < 1 || result.Status > 7 || result.Success != result.ProcessedAt.HasValue ||
                result.ClaimedMailCount < 0 || result.PartiallyClaimedMailCount < 0 || result.NoCapacityMailCount < 0 ||
                processed > 100 || result.GrantedGold < 0 || result.GrantedGem < 0 ||
                result.CurrentGold < 0 || result.CurrentGem < 0 ||
                (result.Status == 5 && processed == 0) ||
                ((!result.Success || result.Status == 1) &&
                 (processed != 0 || items.Count != 0 || result.GrantedGold != 0 || result.GrantedGem != 0 || result.HasMore)) ||
                (!result.Success && (result.CurrentGold != 0 || result.CurrentGem != 0)))
                throw new GameProtocolException("Invalid claim-all status or totals.");
            result.Items = items.AsReadOnly();
            return result;
        }
    }

    // 같은 요청의 프레임만 순서대로 조립하고 메모리 사용량을 제한한다.
    public sealed class MailClaimAllItemAssembler : IDisposable
    {
        public const int MaxPartCount = 64;
        public const int MaxPayloadBytes = 256 * 1024;
        private readonly Guid _requestId;
        private readonly MemoryStream _payload = new MemoryStream();
        private int _nextPart;
        private int _partCount;
        private bool _completed;

        public MailClaimAllItemAssembler(Guid requestId)
        {
            if (requestId == Guid.Empty) throw new ArgumentException("Request id is required.", nameof(requestId));
            _requestId = requestId;
        }

        public bool Append(byte[] frame, out ItemMailClaimAllResponse response)
        {
            response = null;
            if (_completed) throw new GameProtocolException("Claim-all response is already complete.");
            if (frame.Length > GamePacketWriter.MaxPacketSize) throw new GameProtocolException("Oversized claim-all frame.");
            var reader = new GamePacketReader(frame);
            Guid id;
            if (reader.Opcode != GameOpcode.MailClaimAllResult ||
                !Guid.TryParseExact(reader.ReadString(), "D", out id) || id != _requestId)
                throw new GameProtocolException("Unexpected claim-all response request id or opcode.");
            var index = reader.ReadInt();
            var count = reader.ReadInt();
            if (count < 1 || count > MaxPartCount || index != _nextPart ||
                (_nextPart > 0 && count != _partCount) || index >= count ||
                reader.RemainingBytes == 0 || _payload.Length + reader.RemainingBytes > MaxPayloadBytes)
                throw new GameProtocolException("Invalid claim-all part sequence or size.");
            _partCount = count;
            var chunk = reader.ReadBytes(reader.RemainingBytes);
            _payload.Write(chunk, 0, chunk.Length);
            _nextPart++;
            if (_nextPart != _partCount) return false;
            _completed = true;
            response = MailClaimAllItemCodec.ParsePayload(_payload.ToArray(), _requestId);
            return true;
        }

        public void Dispose() { _payload.Dispose(); }
    }
}
