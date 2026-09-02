using System;
using System.Collections.Generic;
using BlueServer.Client.Models;

namespace BlueServer.Client.Protocol
{
    public static class MailPacketCodec
    {
        public const int DefaultPageSize = 20;
        public const int MaxPageSize = 50;

        public static byte[] BuildListRequest(
            int pageSize,
            MailListCursor cursor)
        {
            if (pageSize < 1 || pageSize > MaxPageSize)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            }

            using (var writer = new GamePacketWriter())
            {
                writer.WriteInt(pageSize);
                writer.WriteBool(cursor != null);

                if (cursor != null)
                {
                    ValidateId(cursor.MailId, "Mail cursor id");
                    writer.WriteLong(ToUnixMilliseconds(cursor.SentAt));
                    writer.WriteLong(cursor.MailId);
                }

                return writer.BuildPacket(GameOpcode.MailList);
            }
        }

        public static byte[] BuildDetailRequest(long mailId)
        {
            return BuildMailIdRequest(GameOpcode.MailDetail, mailId);
        }

        public static byte[] BuildReadRequest(long mailId)
        {
            return BuildMailIdRequest(GameOpcode.MailRead, mailId);
        }

        public static byte[] BuildClaimRequest(long mailId)
        {
            return BuildMailIdRequest(GameOpcode.MailClaim, mailId);
        }

        public static byte[] BuildClaimAllRequest()
        {
            using (var writer = new GamePacketWriter())
            {
                return writer.BuildPacket(GameOpcode.MailClaimAll);
            }
        }

        public static MailListResponse ParseListResponse(byte[] packet)
        {
            var reader = CreateReader(packet, GameOpcode.MailListResult);
            var success = reader.ReadBool();
            var message = reader.ReadString();
            var itemCount = reader.ReadInt();

            if (itemCount < 0 || itemCount > MaxPageSize)
            {
                throw new GameProtocolException(
                    "Mail list item count is outside the allowed range.");
            }

            var items = new List<MailListItem>(itemCount);

            for (var index = 0; index < itemCount; index++)
            {
                var id = reader.ReadLong();
                ValidateReceivedId(id, "Mail id");

                var title = reader.ReadString();
                var sentAt = ReadTime(reader);
                var expiresAt = ReadOptionalTime(reader);
                var isRead = reader.ReadBool();
                var isClaimed = reader.ReadBool();
                var isExpired = reader.ReadBool();
                var canClaim = reader.ReadBool();
                var attachmentCount = reader.ReadInt();

                if (attachmentCount < 0)
                {
                    throw new GameProtocolException(
                        "Mail attachment count cannot be negative.");
                }

                items.Add(new MailListItem(
                    id,
                    title,
                    sentAt,
                    expiresAt,
                    isRead,
                    isClaimed,
                    isExpired,
                    canClaim,
                    attachmentCount));
            }

            MailListCursor nextCursor = null;

            if (reader.ReadBool())
            {
                var sentAt = ReadTime(reader);
                var mailId = reader.ReadLong();
                ValidateReceivedId(mailId, "Mail cursor id");
                nextCursor = new MailListCursor(sentAt, mailId);
            }

            reader.EnsureFullyConsumed();
            return new MailListResponse(
                success,
                message,
                items.AsReadOnly(),
                nextCursor);
        }

        public static MailDetailResponse ParseDetailResponse(byte[] packet)
        {
            var reader = CreateReader(packet, GameOpcode.MailDetailResult);
            var success = reader.ReadBool();
            var message = reader.ReadString();
            MailDetail mail = null;

            if (success)
            {
                var id = reader.ReadLong();
                ValidateReceivedId(id, "Mail id");

                var title = reader.ReadString();
                var body = reader.ReadString();
                var sentAt = ReadTime(reader);
                var expiresAt = ReadOptionalTime(reader);
                var readAt = ReadOptionalTime(reader);
                var claimedAt = ReadOptionalTime(reader);
                var isExpired = reader.ReadBool();
                var canClaim = reader.ReadBool();
                var attachmentCount = reader.ReadInt();

                if (attachmentCount < 0 ||
                    attachmentCount > reader.RemainingBytes / (sizeof(int) * 2))
                {
                    throw new GameProtocolException(
                        "Mail attachment count is outside the packet payload range.");
                }

                var attachments = new List<MailAttachment>(attachmentCount);

                for (var index = 0; index < attachmentCount; index++)
                {
                    var rewardType = reader.ReadInt();
                    var amount = reader.ReadInt();

                    if (rewardType <= 0 || amount <= 0)
                    {
                        throw new GameProtocolException(
                            "Mail attachment reward type and amount must be positive.");
                    }

                    attachments.Add(new MailAttachment(rewardType, amount));
                }

                mail = new MailDetail(
                    id,
                    title,
                    body,
                    sentAt,
                    expiresAt,
                    readAt,
                    claimedAt,
                    isExpired,
                    canClaim,
                    attachments.AsReadOnly());
            }

            reader.EnsureFullyConsumed();
            return new MailDetailResponse(success, message, mail);
        }

        public static MailReadResponse ParseReadResponse(byte[] packet)
        {
            var reader = CreateReader(packet, GameOpcode.MailReadResult);
            var success = reader.ReadBool();
            var status = ReadDefinedStatus<MailReadStatus>(reader);
            var message = reader.ReadString();
            var readAt = ReadOptionalTime(reader);
            var successfulStatus = status == MailReadStatus.MarkedAsRead ||
                status == MailReadStatus.AlreadyRead;

            if (success != successfulStatus || success != readAt.HasValue)
            {
                throw new GameProtocolException(
                    "Mail read success, status, and read time do not agree.");
            }

            reader.EnsureFullyConsumed();
            return new MailReadResponse(success, status, message, readAt);
        }

        public static MailClaimResponse ParseClaimResponse(byte[] packet)
        {
            var reader = CreateReader(packet, GameOpcode.MailClaimResult);
            var success = reader.ReadBool();
            var status = ReadDefinedStatus<MailClaimStatus>(reader);
            var message = reader.ReadString();
            var claimedAt = ReadOptionalTime(reader);
            var currentGold = reader.ReadInt();
            var currentGem = reader.ReadInt();
            var successfulStatus = status == MailClaimStatus.Claimed ||
                status == MailClaimStatus.AlreadyClaimed;

            if (success != successfulStatus || success != claimedAt.HasValue)
            {
                throw new GameProtocolException(
                    "Mail claim success, status, and claimed time do not agree.");
            }

            EnsureNonNegative(currentGold, "Current Gold");
            EnsureNonNegative(currentGem, "Current Gem");
            reader.EnsureFullyConsumed();

            return new MailClaimResponse(
                success,
                status,
                message,
                claimedAt,
                currentGold,
                currentGem);
        }

        public static MailClaimAllResponse ParseClaimAllResponse(byte[] packet)
        {
            var reader = CreateReader(packet, GameOpcode.MailClaimAllResult);
            var success = reader.ReadBool();
            var status = ReadDefinedStatus<MailClaimAllStatus>(reader);
            var message = reader.ReadString();
            var claimedMailCount = reader.ReadInt();
            var grantedGold = reader.ReadInt();
            var grantedGem = reader.ReadInt();
            var currentGold = reader.ReadInt();
            var currentGem = reader.ReadInt();
            var hasMore = reader.ReadBool();

            ValidateClaimAll(
                success,
                status,
                claimedMailCount,
                grantedGold,
                grantedGem,
                currentGold,
                currentGem,
                hasMore);

            reader.EnsureFullyConsumed();
            return new MailClaimAllResponse(
                success,
                status,
                message,
                claimedMailCount,
                grantedGold,
                grantedGem,
                currentGold,
                currentGem,
                hasMore);
        }

        private static byte[] BuildMailIdRequest(
            GameOpcode opcode,
            long mailId)
        {
            ValidateId(mailId, nameof(mailId));

            using (var writer = new GamePacketWriter())
            {
                writer.WriteLong(mailId);
                return writer.BuildPacket(opcode);
            }
        }

        private static GamePacketReader CreateReader(
            byte[] packet,
            GameOpcode expectedOpcode)
        {
            var reader = new GamePacketReader(packet);

            if (reader.Opcode != expectedOpcode)
            {
                throw new GameProtocolException(
                    "Received an unexpected Mail packet opcode.");
            }

            return reader;
        }

        private static TStatus ReadDefinedStatus<TStatus>(
            GamePacketReader reader)
            where TStatus : struct
        {
            var value = reader.ReadInt();

            if (!Enum.IsDefined(typeof(TStatus), value))
            {
                throw new GameProtocolException(
                    "Received an undefined Mail packet status.");
            }

            return (TStatus)Enum.ToObject(typeof(TStatus), value);
        }

        private static DateTime? ReadOptionalTime(GamePacketReader reader)
        {
            return reader.ReadBool() ? ReadTime(reader) : (DateTime?)null;
        }

        private static DateTime ReadTime(GamePacketReader reader)
        {
            try
            {
                return DateTimeOffset
                    .FromUnixTimeMilliseconds(reader.ReadLong())
                    .UtcDateTime;
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new GameProtocolException(
                    "Mail packet contains an invalid Unix timestamp.",
                    exception);
            }
        }

        private static long ToUnixMilliseconds(DateTime value)
        {
            if (value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException(
                    "Mail cursor timestamp must use UTC.",
                    nameof(value));
            }

            return new DateTimeOffset(value).ToUnixTimeMilliseconds();
        }

        private static void ValidateId(long value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    "Mail id must be greater than zero.");
            }
        }

        private static void ValidateReceivedId(long value, string name)
        {
            if (value <= 0)
            {
                throw new GameProtocolException(
                    name + " must be greater than zero.");
            }
        }

        private static void EnsureNonNegative(int value, string name)
        {
            if (value < 0)
            {
                throw new GameProtocolException(
                    name + " cannot be negative.");
            }
        }

        private static void ValidateClaimAll(
            bool success,
            MailClaimAllStatus status,
            int claimedMailCount,
            int grantedGold,
            int grantedGem,
            int currentGold,
            int currentGem,
            bool hasMore)
        {
            var successfulStatus = status == MailClaimAllStatus.Claimed ||
                status == MailClaimAllStatus.NothingToClaim;

            if (success != successfulStatus)
            {
                throw new GameProtocolException(
                    "Mail claim-all success and status do not agree.");
            }

            EnsureNonNegative(claimedMailCount, "Claimed Mail count");
            EnsureNonNegative(grantedGold, "Granted Gold");
            EnsureNonNegative(grantedGem, "Granted Gem");
            EnsureNonNegative(currentGold, "Current Gold");
            EnsureNonNegative(currentGem, "Current Gem");

            if (status == MailClaimAllStatus.Claimed && claimedMailCount == 0)
            {
                throw new GameProtocolException(
                    "Claimed result must contain at least one claimed Mail.");
            }

            if (status == MailClaimAllStatus.NothingToClaim &&
                (claimedMailCount != 0 ||
                    grantedGold != 0 ||
                    grantedGem != 0 ||
                    hasMore))
            {
                throw new GameProtocolException(
                    "Nothing-to-claim result contains unexpected rewards.");
            }

            if (!success &&
                (claimedMailCount != 0 ||
                    grantedGold != 0 ||
                    grantedGem != 0 ||
                    currentGold != 0 ||
                    currentGem != 0 ||
                    hasMore))
            {
                throw new GameProtocolException(
                    "Failed claim-all result contains reward or balance data.");
            }
        }
    }
}
