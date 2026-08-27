using blueServer.Domain.Rewards;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Mails;

public sealed class MailDetailQueryService
{
    private readonly GameDbContext _db;

    public MailDetailQueryService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<MailDetailResult> GetAsync(
        long playerId,
        long mailId,
        DateTime currentTime,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(playerId, mailId, currentTime);

        // Mail 조회
        var mail = await _db.Mails
            .AsNoTracking()
            .Where(mail =>
                mail.Id == mailId &&
                mail.PlayerId == playerId)
            .Select(mail => new MailDetail(
                mail.Id,
                mail.Title,
                mail.Body,
                mail.SentAt,
                mail.ExpiresAt,
                mail.ReadAt,
                mail.ClaimedAt,
                mail.ExpiresAt.HasValue &&
                    mail.ExpiresAt.Value <= currentTime,
                mail.Attachments
                    .OrderBy(attachment => attachment.Type)
                    .Select(attachment => new MailDetailAttachment(
                        attachment.Type,
                        attachment.Amount))
                    .ToArray()))
            .FirstOrDefaultAsync(cancellationToken);

        return mail is null
            ? MailDetailResult.NotFound()
            : MailDetailResult.Success(mail);
    }

    private static void ValidateRequest(
        long playerId,
        long mailId,
        DateTime currentTime)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        if (mailId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mailId),
                mailId,
                "Mail id must be greater than zero.");
        }

        if (currentTime.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Current time must use UTC.",
                nameof(currentTime));
        }
    }
}

public sealed record MailDetail(
    long Id,
    string Title,
    string Body,
    DateTime SentAt,
    DateTime? ExpiresAt,
    DateTime? ReadAt,
    DateTime? ClaimedAt,
    bool IsExpired,
    IReadOnlyList<MailDetailAttachment> Attachments)
{
    public bool IsRead => ReadAt.HasValue;
    public bool IsClaimed => ClaimedAt.HasValue;
    public bool CanClaim =>
        Attachments.Count > 0 &&
        !IsClaimed &&
        !IsExpired;
}

public sealed record MailDetailAttachment(
    RewardType Type,
    int Amount);

public enum MailDetailStatus
{
    Success = 0,
    NotFound = 1
}

public sealed record MailDetailResult(
    MailDetailStatus Status,
    MailDetail? Mail)
{
    public bool IsSuccess => Status == MailDetailStatus.Success;

    public static MailDetailResult Success(MailDetail mail)
    {
        ArgumentNullException.ThrowIfNull(mail);

        return new MailDetailResult(
            MailDetailStatus.Success,
            mail);
    }

    public static MailDetailResult NotFound()
    {
        return new MailDetailResult(
            MailDetailStatus.NotFound,
            null);
    }
}
