using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Mails;

public sealed class MailDeliveryService
{
    private readonly GameDbContext _db;

    public MailDeliveryService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<MailDeliveryResult> DeliverAsync(
        MailDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var candidate = CreateCandidate(request);
        var existingResult = await TryResolveExistingAsync(
            candidate,
            cancellationToken);

        if (existingResult is not null)
        {
            return existingResult;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(
            cancellationToken);

        try
        {
            var result = await DeliverWithinCurrentTransactionCoreAsync(
                candidate,
                cancellationToken);

            if (result.Status == MailDeliveryStatus.Delivered)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
            }

            return result;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();

            // Unique Constraint 경합이면 먼저 완료된 동일 발송 결과로 복구
            var concurrentResult = await TryResolveExistingAsync(
                candidate,
                CancellationToken.None);

            if (concurrentResult is not null)
            {
                return concurrentResult;
            }

            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<MailDeliveryResult> DeliverWithinCurrentTransactionAsync(
        MailDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        // 이벤트 완료 등의 상위 Use Case와 같은 Transaction 사용 강제
        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "An active transaction is required to deliver mail within a parent operation.");
        }

        var candidate = CreateCandidate(request);

        return await DeliverWithinCurrentTransactionCoreAsync(
            candidate,
            cancellationToken);
    }

    private async Task<MailDeliveryResult> DeliverWithinCurrentTransactionCoreAsync(
        Mail candidate,
        CancellationToken cancellationToken)
    {
        var existingResult = await TryResolveExistingAsync(
            candidate,
            cancellationToken);

        if (existingResult is not null)
        {
            return existingResult;
        }

        var playerExists = await _db.Players
            .AsNoTracking()
            .AnyAsync(
                player => player.Id == candidate.PlayerId,
                cancellationToken);

        if (!playerExists)
        {
            return MailDeliveryResult.PlayerNotFound();
        }

        _db.Mails.Add(candidate);
        await _db.SaveChangesAsync(cancellationToken);

        return MailDeliveryResult.Delivered(candidate.Id);
    }

    private async Task<MailDeliveryResult?> TryResolveExistingAsync(
        Mail candidate,
        CancellationToken cancellationToken)
    {
        var existingMail = await _db.Mails
            .AsNoTracking()
            .Include(mail => mail.Attachments)
            .FirstOrDefaultAsync(
                mail =>
                    mail.PlayerId == candidate.PlayerId &&
                    mail.SourceType == candidate.SourceType &&
                    mail.SourceId == candidate.SourceId,
                cancellationToken);

        if (existingMail is null)
        {
            return null;
        }

        return existingMail.HasSameDelivery(candidate)
            ? MailDeliveryResult.AlreadyDelivered(existingMail.Id)
            : MailDeliveryResult.IdempotencyConflict(existingMail.Id);
    }

    private static Mail CreateCandidate(MailDeliveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sentAt = NormalizeUtcToMicroseconds(
            request.SentAt,
            nameof(request.SentAt));
        DateTime? expiresAt = request.ExpiresAt.HasValue
            ? NormalizeUtcToMicroseconds(
                request.ExpiresAt.Value,
                nameof(request.ExpiresAt))
            : null;

        return Mail.Create(
            request.PlayerId,
            request.Title,
            request.Body,
            sentAt,
            expiresAt,
            request.Rewards,
            request.SourceType,
            request.SourceId);
    }

    private static DateTime NormalizeUtcToMicroseconds(
        DateTime value,
        string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Mail delivery timestamps must use UTC.",
                parameterName);
        }

        return new DateTime(
            value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond,
            DateTimeKind.Utc);
    }
}

public sealed record MailDeliveryRequest(
    long PlayerId,
    MailSourceType SourceType,
    string SourceId,
    string Title,
    string Body,
    DateTime SentAt,
    DateTime? ExpiresAt,
    IReadOnlyList<RewardItem>? Rewards = null);

public enum MailDeliveryStatus
{
    Delivered = 0,
    AlreadyDelivered = 1,
    PlayerNotFound = 2,
    IdempotencyConflict = 3
}

public sealed record MailDeliveryResult(
    MailDeliveryStatus Status,
    long? MailId)
{
    public bool IsSuccess => Status is
        MailDeliveryStatus.Delivered or
        MailDeliveryStatus.AlreadyDelivered;

    public static MailDeliveryResult Delivered(long mailId)
    {
        return Success(MailDeliveryStatus.Delivered, mailId);
    }

    public static MailDeliveryResult AlreadyDelivered(long mailId)
    {
        return Success(MailDeliveryStatus.AlreadyDelivered, mailId);
    }

    public static MailDeliveryResult PlayerNotFound()
    {
        return new MailDeliveryResult(
            MailDeliveryStatus.PlayerNotFound,
            null);
    }

    public static MailDeliveryResult IdempotencyConflict(long mailId)
    {
        return new MailDeliveryResult(
            MailDeliveryStatus.IdempotencyConflict,
            mailId);
    }

    private static MailDeliveryResult Success(
        MailDeliveryStatus status,
        long mailId)
    {
        if (mailId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mailId),
                mailId,
                "Mail id must be greater than zero.");
        }

        return new MailDeliveryResult(status, mailId);
    }
}
