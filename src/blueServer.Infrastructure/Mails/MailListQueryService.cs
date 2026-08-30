using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Mails;

public sealed class MailListQueryService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    private readonly GameDbContext _db;

    public MailListQueryService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<MailListResult> GetAsync(
        long playerId,
        DateTime currentTime,
        int pageSize = DefaultPageSize,
        MailListCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(playerId, currentTime, pageSize, cursor);

        var playerExists = await _db.Players
            .AsNoTracking()
            .AnyAsync(
                player => player.Id == playerId,
                cancellationToken);

        if (!playerExists)
        {
            return MailListResult.PlayerNotFound();
        }

        var query = _db.Mails
            .AsNoTracking()
            .Where(mail => mail.PlayerId == playerId);

        if (cursor is not null)
        {
            // 동일 SentAt Mail의 누락 방지를 위한 Id 기반 Keyset 보조 정렬
            query = query.Where(mail =>
                mail.SentAt < cursor.SentAt ||
                (mail.SentAt == cursor.SentAt && mail.Id < cursor.Id));
        }

        // 다음 Page 존재 여부 확인을 위해 요청 크기보다 한 건 추가 조회
        var rows = await query
            .OrderByDescending(mail => mail.SentAt)
            .ThenByDescending(mail => mail.Id)
            .Select(mail => new MailListItem(
                mail.Id,
                mail.Title,
                mail.SentAt,
                mail.ExpiresAt,
                mail.ReadAt.HasValue,
                mail.ClaimedAt.HasValue,
                mail.ExpiresAt.HasValue &&
                    mail.ExpiresAt.Value <= currentTime,
                mail.Attachments.Count))
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasNextPage = rows.Count > pageSize;

        if (hasNextPage)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var nextCursor = hasNextPage
            ? new MailListCursor(rows[^1].SentAt, rows[^1].Id)
            : null;

        return MailListResult.Success(rows, nextCursor);
    }

    private static void ValidateRequest(
        long playerId,
        DateTime currentTime,
        int pageSize,
        MailListCursor? cursor)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        if (currentTime.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Current time must use UTC.",
                nameof(currentTime));
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                pageSize,
                $"Page size must be between 1 and {MaxPageSize}.");
        }

        if (cursor is null)
        {
            return;
        }

        if (cursor.Id <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cursor),
                cursor,
                "Cursor id must be greater than zero.");
        }

        if (cursor.SentAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Cursor sent time must use UTC.",
                nameof(cursor));
        }
    }
}

public sealed record MailListCursor(
    DateTime SentAt,
    long Id);

public sealed record MailListItem(
    long Id,
    string Title,
    DateTime SentAt,
    DateTime? ExpiresAt,
    bool IsRead,
    bool IsClaimed,
    bool IsExpired,
    int AttachmentCount)
{
    public bool CanClaim =>
        AttachmentCount > 0 &&
        !IsClaimed &&
        !IsExpired;
}

public enum MailListStatus
{
    Success = 0,
    PlayerNotFound = 1
}

public sealed record MailListResult(
    MailListStatus Status,
    IReadOnlyList<MailListItem> Items,
    MailListCursor? NextCursor)
{
    public bool IsSuccess => Status == MailListStatus.Success;

    public static MailListResult Success(
        IReadOnlyList<MailListItem> items,
        MailListCursor? nextCursor)
    {
        return new MailListResult(
            MailListStatus.Success,
            items,
            nextCursor);
    }

    public static MailListResult PlayerNotFound()
    {
        return new MailListResult(
            MailListStatus.PlayerNotFound,
            [],
            null);
    }
}
