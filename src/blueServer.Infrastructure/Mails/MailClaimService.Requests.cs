using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace blueServer.Infrastructure.Mails;

public sealed partial class MailClaimService
{
    // 단일 수령의 Transaction, 저장, 커밋 및 충돌 복구를 담당한다.
    public async Task<MailClaimRequestResult> ClaimAsync(
        long playerId,
        MailClaimRequest request,
        DateTime processedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(playerId, request.MailId, processedAt);
        processedAt = new DateTime(processedAt.Ticks - processedAt.Ticks % 10, DateTimeKind.Utc);

        var existing = await FindRequestResultAsync(playerId, request, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var prepared = await PrepareWithinCurrentTransactionAsync(
                playerId,
                request,
                processedAt,
                cancellationToken);

            if (prepared.Record is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                return prepared.Result;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return prepared.Result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return await FindRequestResultAsync(playerId, request, CancellationToken.None)
                ?? MailClaimRequestResult.Failure(MailClaimRequestStatus.ConcurrencyConflict);
        }
        catch (DbUpdateException exception) when (IsExpectedClaimCollision(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return await FindRequestResultAsync(playerId, request, CancellationToken.None)
                ?? MailClaimRequestResult.Failure(MailClaimRequestStatus.ConcurrencyConflict);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<MailClaimRequestResult?> FindRequestResultAsync(
        long playerId, MailClaimRequest request, CancellationToken cancellationToken)
    {
        var record = await _db.MailClaimRecords.AsNoTracking().Include(record => record.Items)
            .SingleOrDefaultAsync(record => record.PlayerId == playerId && record.RequestId == request.RequestId,
                cancellationToken);
        return record is null ? null
            : record.MailId == request.MailId ? MailClaimRequestResult.FromRecord(record)
            : MailClaimRequestResult.Failure(MailClaimRequestStatus.IdempotencyConflict);
    }

    internal static bool IsExpectedClaimCollision(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_MailClaimRecords_PlayerId_RequestId"
                or "IX_PlayerItems_PlayerId_ItemTemplateId"
                or "IX_RewardGrantRecords_PlayerId_RequestId"
                or "IX_CurrencyChangeLogs_PlayerId_RequestId_CurrencyType"
                or "IX_InventoryItemChangeLogs_PlayerId_RequestId_ItemTemplateId"
        };
}
