using System;
using System.Collections.Generic;

namespace BlueServer.Client.Models
{
    public sealed class MailListCursor
    {
        public MailListCursor(DateTime sentAt, long mailId)
        {
            SentAt = sentAt;
            MailId = mailId;
        }

        public DateTime SentAt { get; private set; }
        public long MailId { get; private set; }
    }

    public sealed class MailListItem
    {
        public MailListItem(
            long id,
            string title,
            DateTime sentAt,
            DateTime? expiresAt,
            bool isRead,
            bool isClaimed,
            bool isExpired,
            bool canClaim,
            int attachmentCount)
        {
            Id = id;
            Title = title;
            SentAt = sentAt;
            ExpiresAt = expiresAt;
            IsRead = isRead;
            IsClaimed = isClaimed;
            IsExpired = isExpired;
            CanClaim = canClaim;
            AttachmentCount = attachmentCount;
        }

        public long Id { get; private set; }
        public string Title { get; private set; }
        public DateTime SentAt { get; private set; }
        public DateTime? ExpiresAt { get; private set; }
        public bool IsRead { get; private set; }
        public bool IsClaimed { get; private set; }
        public bool IsExpired { get; private set; }
        public bool CanClaim { get; private set; }
        public int AttachmentCount { get; private set; }
    }

    public sealed class MailListResponse
    {
        public MailListResponse(
            bool success,
            string message,
            IReadOnlyList<MailListItem> items,
            MailListCursor nextCursor)
        {
            Success = success;
            Message = message;
            Items = items;
            NextCursor = nextCursor;
        }

        public bool Success { get; private set; }
        public string Message { get; private set; }
        public IReadOnlyList<MailListItem> Items { get; private set; }
        public MailListCursor NextCursor { get; private set; }
    }

    public sealed class MailAttachment
    {
        public MailAttachment(int rewardType, int amount)
        {
            RewardType = rewardType;
            Amount = amount;
        }

        public int RewardType { get; private set; }
        public int Amount { get; private set; }
    }

    public sealed class MailDetail
    {
        public MailDetail(
            long id,
            string title,
            string body,
            DateTime sentAt,
            DateTime? expiresAt,
            DateTime? readAt,
            DateTime? claimedAt,
            bool isExpired,
            bool canClaim,
            IReadOnlyList<MailAttachment> attachments)
        {
            Id = id;
            Title = title;
            Body = body;
            SentAt = sentAt;
            ExpiresAt = expiresAt;
            ReadAt = readAt;
            ClaimedAt = claimedAt;
            IsExpired = isExpired;
            CanClaim = canClaim;
            Attachments = attachments;
        }

        public long Id { get; private set; }
        public string Title { get; private set; }
        public string Body { get; private set; }
        public DateTime SentAt { get; private set; }
        public DateTime? ExpiresAt { get; private set; }
        public DateTime? ReadAt { get; private set; }
        public DateTime? ClaimedAt { get; private set; }
        public bool IsExpired { get; private set; }
        public bool CanClaim { get; private set; }
        public IReadOnlyList<MailAttachment> Attachments { get; private set; }
    }

    public sealed class MailDetailResponse
    {
        public MailDetailResponse(
            bool success,
            string message,
            MailDetail mail)
        {
            Success = success;
            Message = message;
            Mail = mail;
        }

        public bool Success { get; private set; }
        public string Message { get; private set; }
        public MailDetail Mail { get; private set; }
    }

    public enum MailReadStatus
    {
        MarkedAsRead = 0,
        AlreadyRead = 1,
        NotFound = 2,
        ConcurrencyConflict = 3
    }

    public sealed class MailReadResponse
    {
        public MailReadResponse(
            bool success,
            MailReadStatus status,
            string message,
            DateTime? readAt)
        {
            Success = success;
            Status = status;
            Message = message;
            ReadAt = readAt;
        }

        public bool Success { get; private set; }
        public MailReadStatus Status { get; private set; }
        public string Message { get; private set; }
        public DateTime? ReadAt { get; private set; }
    }

    public enum MailClaimStatus
    {
        Claimed = 0,
        AlreadyClaimed = 1,
        NotFound = 2,
        Expired = 3,
        NoRewards = 4,
        ConcurrencyConflict = 5,
        IdempotencyConflict = 6
    }

    public sealed class MailClaimResponse
    {
        public MailClaimResponse(
            bool success,
            MailClaimStatus status,
            string message,
            DateTime? claimedAt,
            int currentGold,
            int currentGem)
        {
            Success = success;
            Status = status;
            Message = message;
            ClaimedAt = claimedAt;
            CurrentGold = currentGold;
            CurrentGem = currentGem;
        }

        public bool Success { get; private set; }
        public MailClaimStatus Status { get; private set; }
        public string Message { get; private set; }
        public DateTime? ClaimedAt { get; private set; }
        public int CurrentGold { get; private set; }
        public int CurrentGem { get; private set; }
    }

    public enum MailClaimAllStatus
    {
        Claimed = 0,
        NothingToClaim = 1,
        PlayerNotFound = 2,
        ConcurrencyConflict = 3,
        IdempotencyConflict = 4
    }

    public sealed class MailClaimAllResponse
    {
        public MailClaimAllResponse(
            bool success,
            MailClaimAllStatus status,
            string message,
            int claimedMailCount,
            int grantedGold,
            int grantedGem,
            int currentGold,
            int currentGem,
            bool hasMore)
        {
            Success = success;
            Status = status;
            Message = message;
            ClaimedMailCount = claimedMailCount;
            GrantedGold = grantedGold;
            GrantedGem = grantedGem;
            CurrentGold = currentGold;
            CurrentGem = currentGem;
            HasMore = hasMore;
        }

        public bool Success { get; private set; }
        public MailClaimAllStatus Status { get; private set; }
        public string Message { get; private set; }
        public int ClaimedMailCount { get; private set; }
        public int GrantedGold { get; private set; }
        public int GrantedGem { get; private set; }
        public int CurrentGold { get; private set; }
        public int CurrentGem { get; private set; }
        public bool HasMore { get; private set; }
    }
}
