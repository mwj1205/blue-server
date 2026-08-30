using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Mails;

public sealed class MailReadService
{
    private readonly GameDbContext _db;

    public MailReadService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<MailReadResult> MarkAsReadAsync(
        long playerId,
        long mailId,
        DateTime readAt,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(playerId, mailId, readAt);

        // 다른 Player의 Mail 존재 여부를 노출하지 않는 소유권 포함 조회
        var mail = await _db.Mails
            .FirstOrDefaultAsync(
                mail =>
                    mail.Id == mailId &&
                    mail.PlayerId == playerId,
                cancellationToken);

        if (mail is null)
        {
            return MailReadResult.NotFound();
        }

        if (mail.ReadAt.HasValue)
        {
            return MailReadResult.AlreadyRead(mail.ReadAt.Value);
        }

        mail.MarkAsRead(readAt);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);

            return MailReadResult.MarkedAsRead(readAt);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // 실패한 Entity가 이후 SaveChanges에 다시 포함되지 않도록 추적 해제
            foreach (var entry in exception.Entries)
            {
                entry.State = EntityState.Detached;
            }

            return await ResolveConcurrencyResultAsync(
                playerId,
                mailId,
                cancellationToken);
        }
    }

    private async Task<MailReadResult> ResolveConcurrencyResultAsync(
        long playerId,
        long mailId,
        CancellationToken cancellationToken)
    {
        var currentMail = await _db.Mails
            .AsNoTracking()
            .Where(mail =>
                mail.Id == mailId &&
                mail.PlayerId == playerId)
            .Select(mail => new
            {
                mail.ReadAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (currentMail is null)
        {
            return MailReadResult.NotFound();
        }

        return currentMail.ReadAt.HasValue
            ? MailReadResult.AlreadyRead(currentMail.ReadAt.Value)
            : MailReadResult.ConcurrencyConflict();
    }

    private static void ValidateRequest(
        long playerId,
        long mailId,
        DateTime readAt)
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

        if (readAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Read time must use UTC.",
                nameof(readAt));
        }
    }
}

public enum MailReadStatus
{
    MarkedAsRead = 0,
    AlreadyRead = 1,
    NotFound = 2,
    ConcurrencyConflict = 3
}

public sealed record MailReadResult(
    MailReadStatus Status,
    DateTime? ReadAt)
{
    public bool IsSuccess =>
        Status is MailReadStatus.MarkedAsRead or
            MailReadStatus.AlreadyRead;

    public static MailReadResult MarkedAsRead(DateTime readAt)
    {
        return new MailReadResult(
            MailReadStatus.MarkedAsRead,
            readAt);
    }

    public static MailReadResult AlreadyRead(DateTime readAt)
    {
        return new MailReadResult(
            MailReadStatus.AlreadyRead,
            readAt);
    }

    public static MailReadResult NotFound()
    {
        return new MailReadResult(
            MailReadStatus.NotFound,
            null);
    }

    public static MailReadResult ConcurrencyConflict()
    {
        return new MailReadResult(
            MailReadStatus.ConcurrencyConflict,
            null);
    }
}
