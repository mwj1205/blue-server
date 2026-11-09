using blueServer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace blueServer.Infrastructure.Mails;

public sealed partial class MailClaimAllService
{
    public async Task<MailClaimAllRequestResult> ClaimAllAsync(
        long playerId,
        MailClaimAllRequest request,
        DateTime processedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(playerId, processedAt);
        processedAt = new DateTime(processedAt.Ticks - processedAt.Ticks % 10, DateTimeKind.Utc);

        var existing = await FindBatchResultAsync(playerId, request.RequestId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var player = await _db.Players.SingleOrDefaultAsync(
                player => player.Id == playerId,
                cancellationToken);
            if (player is null)
            {
                return MailClaimAllRequestResult.Failure(MailClaimAllRequestStatus.PlayerNotFound);
            }

            var candidates = await _db.Mails
                .Include(mail => mail.Attachments)
                .Include(mail => mail.ItemAttachments)
                .AsSplitQuery()
                .Where(mail =>
                    mail.PlayerId == playerId &&
                    !mail.ClaimedAt.HasValue &&
                    mail.SentAt <= processedAt &&
                    (!mail.ExpiresAt.HasValue || mail.ExpiresAt.Value > processedAt) &&
                    (mail.Attachments.Any() ||
                        mail.ItemAttachments.Any(item => item.Quantity > item.ClaimedQuantity)))
                .OrderBy(mail => mail.SentAt)
                .ThenBy(mail => mail.Id)
                .Take(MailClaimAllRecord.MaxMailCount + 1)
                .ToArrayAsync(cancellationToken);

            var claims = new List<MailClaimRecord>();
            foreach (var mail in candidates.Take(MailClaimAllRecord.MaxMailCount))
            {
                var prepared = await _mailClaimService.PrepareTrackedMailAsync(
                    mail,
                    Guid.NewGuid(),
                    processedAt,
                    cancellationToken);
                if (prepared.Record is null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    _db.ChangeTracker.Clear();
                    return MailClaimAllRequestResult.Failure(MapPreparationFailure(prepared.Result.Status));
                }

                claims.Add(prepared.Record);
            }

            var record = MailClaimAllRecord.Create(
                playerId,
                request.RequestId,
                processedAt,
                player.Gold,
                player.Gem,
                candidates.Length > MailClaimAllRecord.MaxMailCount,
                claims);
            _db.MailClaimAllRecords.Add(record);
            var result = MailClaimAllRequestResult.FromRecord(record);

            // 빈 결과도 저장하여 같은 RequestId로 이후 도착한 Mail을 수령하지 않도록 한다.
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (IsBatchCollision(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return await FindBatchResultAsync(playerId, request.RequestId, CancellationToken.None)
                ?? MailClaimAllRequestResult.Failure(MailClaimAllRequestStatus.ConcurrencyConflict);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<MailClaimAllRequestResult?> FindBatchResultAsync(
        long playerId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var record = await _db.MailClaimAllRecords
            .AsNoTracking()
            .Include(batch => batch.Entries)
            .ThenInclude(entry => entry.MailClaimRecord)
            .ThenInclude(claim => claim.Items)
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                batch => batch.PlayerId == playerId && batch.RequestId == requestId,
                cancellationToken);
        return record is null ? null : MailClaimAllRequestResult.FromRecord(record);
    }

    private static bool IsBatchCollision(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException ||
        MailClaimService.IsExpectedClaimCollision(exception) ||
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_MailClaimAllRecords_PlayerId_RequestId"
        };

    private static MailClaimAllRequestStatus MapPreparationFailure(MailClaimRequestStatus status) =>
        status switch
        {
            MailClaimRequestStatus.PlayerNotFound => MailClaimAllRequestStatus.PlayerNotFound,
            MailClaimRequestStatus.IdempotencyConflict => MailClaimAllRequestStatus.IdempotencyConflict,
            MailClaimRequestStatus.ItemTemplateNotFound => MailClaimAllRequestStatus.ItemTemplateNotFound,
            MailClaimRequestStatus.ItemTemplateInactive => MailClaimAllRequestStatus.ItemTemplateInactive,
            MailClaimRequestStatus.AlreadyClaimed or
                MailClaimRequestStatus.Expired or
                MailClaimRequestStatus.NoRewards => MailClaimAllRequestStatus.ConcurrencyConflict,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unexpected Mail preparation status.")
        };
}
