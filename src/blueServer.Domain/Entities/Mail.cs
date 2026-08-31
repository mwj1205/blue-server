using blueServer.Domain.Rewards;

namespace blueServer.Domain.Entities;

public sealed class Mail
{
    public const int MaxSourceIdLength = 200;
    public const int MaxTitleLength = 100;
    public const int MaxBodyLength = 2_000;

    public long Id { get; set; }
    public long PlayerId { get; set; }
    public MailSourceType SourceType { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public uint Version { get; set; }

    public Player? Player { get; set; }
    public ICollection<MailAttachment> Attachments { get; set; } =
        new List<MailAttachment>();

    public bool IsRead => ReadAt.HasValue;
    public bool IsClaimed => ClaimedAt.HasValue;
    public bool HasAttachments => Attachments.Count > 0;

    public static Mail Create(
        long playerId,
        string title,
        string body,
        DateTime sentAt,
        DateTime? expiresAt = null,
        IEnumerable<RewardItem>? rewards = null,
        MailSourceType sourceType = MailSourceType.System,
        string? sourceId = null)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        if (!Enum.IsDefined(sourceType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceType),
                sourceType,
                "Mail source type is not supported.");
        }

        var normalizedSourceId = ValidateAndNormalizeText(
            sourceId ?? $"direct:{Guid.NewGuid():N}",
            MaxSourceIdLength,
            nameof(sourceId));

        var normalizedTitle = ValidateAndNormalizeText(
            title,
            MaxTitleLength,
            nameof(title));
        var normalizedBody = ValidateAndNormalizeText(
            body,
            MaxBodyLength,
            nameof(body));

        ValidateUtc(sentAt, nameof(sentAt));

        if (expiresAt.HasValue)
        {
            ValidateUtc(expiresAt.Value, nameof(expiresAt));

            if (expiresAt.Value <= sentAt)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expiresAt),
                    expiresAt,
                    "Mail expiration must be later than sent time.");
            }
        }

        var mail = new Mail
        {
            PlayerId = playerId,
            SourceType = sourceType,
            SourceId = normalizedSourceId,
            Title = normalizedTitle,
            Body = normalizedBody,
            SentAt = sentAt,
            ExpiresAt = expiresAt
        };

        if (rewards is null)
        {
            return mail;
        }

        var rewardItems = rewards.ToArray();

        if (rewardItems.Any(reward => reward is null))
        {
            throw new ArgumentException(
                "Mail rewards must not contain null.",
                nameof(rewards));
        }

        // 발송 시점 RewardType별 합산 결과를 Attachment Snapshot으로 저장
        foreach (var rewardGroup in rewardItems.GroupBy(reward => reward.Type))
        {
            var totalAmount = rewardGroup.Aggregate(
                0,
                (total, reward) => checked(total + reward.Amount));

            mail.Attachments.Add(MailAttachment.Create(
                rewardGroup.Key,
                totalAmount));
        }

        return mail;
    }

    public bool HasSameDelivery(Mail other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return PlayerId == other.PlayerId &&
            SourceType == other.SourceType &&
            SourceId == other.SourceId &&
            Title == other.Title &&
            Body == other.Body &&
            SentAt == other.SentAt &&
            ExpiresAt == other.ExpiresAt &&
            HasSameAttachments(other.Attachments);
    }

    public bool IsExpired(DateTime currentTime)
    {
        ValidateUtc(currentTime, nameof(currentTime));

        return ExpiresAt.HasValue &&
            ExpiresAt.Value <= currentTime;
    }

    public void MarkAsRead(DateTime readAt)
    {
        ValidateActionTime(readAt, nameof(readAt));

        // 최초 읽음 시각 보존을 위한 Idempotent 처리
        ReadAt ??= readAt;
    }

    public void Claim(DateTime claimedAt)
    {
        ValidateActionTime(claimedAt, nameof(claimedAt));

        if (!HasAttachments)
        {
            throw new InvalidOperationException(
                "Mail without attachments cannot be claimed.");
        }

        if (IsClaimed)
        {
            throw new InvalidOperationException(
                "Mail rewards have already been claimed.");
        }

        if (IsExpired(claimedAt))
        {
            throw new InvalidOperationException(
                "Expired mail rewards cannot be claimed.");
        }

        ClaimedAt = claimedAt;
        ReadAt ??= claimedAt;
    }

    private void ValidateActionTime(
        DateTime actionTime,
        string parameterName)
    {
        ValidateUtc(actionTime, parameterName);

        if (actionTime < SentAt)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                actionTime,
                "Mail action time must not be earlier than sent time.");
        }
    }

    private bool HasSameAttachments(
        IEnumerable<MailAttachment> otherAttachments)
    {
        var currentItems = Attachments
            .OrderBy(attachment => attachment.Type)
            .Select(attachment => (attachment.Type, attachment.Amount));
        var otherItems = otherAttachments
            .OrderBy(attachment => attachment.Type)
            .Select(attachment => (attachment.Type, attachment.Amount));

        return currentItems.SequenceEqual(otherItems);
    }

    private static string ValidateAndNormalizeText(
        string value,
        int maxLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Mail text is required.",
                parameterName);
        }

        var normalizedValue = value.Trim();

        if (normalizedValue.Length > maxLength)
        {
            throw new ArgumentException(
                $"Mail text must not exceed {maxLength} characters.",
                parameterName);
        }

        return normalizedValue;
    }

    private static void ValidateUtc(
        DateTime value,
        string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Mail timestamps must use UTC.",
                parameterName);
        }
    }
}
