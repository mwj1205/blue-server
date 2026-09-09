using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Currencies;

public sealed class CurrencyChangeService
{
    private readonly GameDbContext _db;

    public CurrencyChangeService(GameDbContext db)
    {
        _db = db;
    }

    public CurrencyChangeResult ChangeWithinCurrentTransaction(
        Player player,
        CurrencyChangeRequest request,
        RewardGrantRecord? rewardGrantRecord = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "An active transaction is required to change currency within a parent operation.");
        }

        if (_db.Entry(player).State == EntityState.Detached)
        {
            throw new InvalidOperationException(
                "The Player must be tracked by the same GameDbContext as the currency change.");
        }

        var balanceBefore = GetBalance(player, request.CurrencyType);
        var balanceAfter = (long)balanceBefore + request.Delta;

        if (balanceAfter < 0)
        {
            return CurrencyChangeResult.InsufficientBalance(balanceBefore);
        }

        var change = CurrencyChangeLog.Create(
            player.Id,
            request.CurrencyType,
            request.Delta,
            balanceBefore,
            request.ReasonType,
            request.SourceId,
            request.RequestId,
            request.ChangedAt,
            rewardGrantRecord);

        ApplyChange(player, request.CurrencyType, request.Delta);
        _db.CurrencyChangeLogs.Add(change);

        return CurrencyChangeResult.Changed(change);
    }

    private static int GetBalance(
        Player player,
        CurrencyType currencyType)
    {
        return currencyType switch
        {
            CurrencyType.Gold => player.Gold,
            CurrencyType.Gem => player.Gem,
            _ => throw new ArgumentOutOfRangeException(
                nameof(currencyType),
                currencyType,
                "Currency type is not supported.")
        };
    }

    private static void ApplyChange(
        Player player,
        CurrencyType currencyType,
        int delta)
    {
        if (delta > 0)
        {
            switch (currencyType)
            {
                case CurrencyType.Gold:
                    player.AddGold(delta);
                    return;

                case CurrencyType.Gem:
                    player.AddGems(delta);
                    return;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(currencyType),
                        currencyType,
                        "Currency type is not supported.");
            }
        }

        var amount = checked(-delta);
        var spent = currencyType switch
        {
            CurrencyType.Gold => player.TrySpendGold(amount),
            CurrencyType.Gem => player.TrySpendGems(amount),
            _ => throw new ArgumentOutOfRangeException(
                nameof(currencyType),
                currencyType,
                "Currency type is not supported.")
        };

        if (!spent)
        {
            throw new InvalidOperationException(
                "Currency balance changed after the available balance was checked.");
        }
    }

    private static void ValidateRequest(CurrencyChangeRequest request)
    {
        if (!Enum.IsDefined(request.CurrencyType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.CurrencyType,
                "Currency type is not supported.");
        }

        if (request.Delta == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Delta,
                "Currency delta must not be zero.");
        }

        if (!Enum.IsDefined(request.ReasonType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.ReasonType,
                "Currency change reason type is not supported.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceId))
        {
            throw new ArgumentException(
                "Currency change source id is required.",
                nameof(request));
        }

        if (request.SourceId.Trim().Length > CurrencyChangeLog.MaxSourceIdLength)
        {
            throw new ArgumentException(
                $"Currency change source id must not exceed {CurrencyChangeLog.MaxSourceIdLength} characters.",
                nameof(request));
        }

        if (request.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request id must not be empty.",
                nameof(request));
        }

        if (request.ChangedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Currency change time must use UTC.",
                nameof(request));
        }
    }
}

public sealed record CurrencyChangeRequest(
    CurrencyType CurrencyType,
    int Delta,
    CurrencyChangeReasonType ReasonType,
    string SourceId,
    Guid RequestId,
    DateTime ChangedAt);

public enum CurrencyChangeStatus
{
    Changed = 0,
    InsufficientBalance = 1
}

public sealed record CurrencyChangeResult(
    CurrencyChangeStatus Status,
    int CurrentBalance,
    CurrencyChangeLog? Change)
{
    public bool IsSuccess => Status == CurrencyChangeStatus.Changed;

    public static CurrencyChangeResult Changed(CurrencyChangeLog change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new CurrencyChangeResult(
            CurrencyChangeStatus.Changed,
            change.BalanceAfter,
            change);
    }

    public static CurrencyChangeResult InsufficientBalance(
        int currentBalance)
    {
        if (currentBalance < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentBalance),
                currentBalance,
                "Currency balance must not be negative.");
        }

        return new CurrencyChangeResult(
            CurrencyChangeStatus.InsufficientBalance,
            currentBalance,
            null);
    }
}
