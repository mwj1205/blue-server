using blueServer.Domain.Currencies;

namespace blueServer.Domain.Entities;

public sealed class CurrencyChangeLog
{
    public const int MaxSourceIdLength = 200;

    public long Id { get; private set; }
    public long PlayerId { get; private set; }
    public CurrencyType CurrencyType { get; private set; }
    public int Delta { get; private set; }
    public int BalanceBefore { get; private set; }
    public int BalanceAfter { get; private set; }
    public CurrencyChangeReasonType ReasonType { get; private set; }
    public string SourceId { get; private set; } = string.Empty;
    public Guid RequestId { get; private set; }
    public long? RewardGrantRecordId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public Player? Player { get; private set; }
    public RewardGrantRecord? RewardGrantRecord { get; private set; }

    private CurrencyChangeLog()
    {
    }

    public static CurrencyChangeLog Create(
        long playerId,
        CurrencyType currencyType,
        int delta,
        int balanceBefore,
        CurrencyChangeReasonType reasonType,
        string sourceId,
        Guid requestId,
        DateTime createdAt,
        RewardGrantRecord? rewardGrantRecord = null)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        if (!Enum.IsDefined(currencyType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(currencyType),
                currencyType,
                "Currency type is not supported.");
        }

        if (delta == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                delta,
                "Currency delta must not be zero.");
        }

        if (balanceBefore < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(balanceBefore),
                balanceBefore,
                "Currency balance must not be negative.");
        }

        if (!Enum.IsDefined(reasonType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reasonType),
                reasonType,
                "Currency change reason type is not supported.");
        }

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException(
                "Currency change source id is required.",
                nameof(sourceId));
        }

        var normalizedSourceId = sourceId.Trim();

        if (normalizedSourceId.Length > MaxSourceIdLength)
        {
            throw new ArgumentException(
                $"Currency change source id must not exceed {MaxSourceIdLength} characters.",
                nameof(sourceId));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request id must not be empty.",
                nameof(requestId));
        }

        if (createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Currency change time must use UTC.",
                nameof(createdAt));
        }

        if (rewardGrantRecord is not null &&
            rewardGrantRecord.PlayerId != playerId)
        {
            throw new ArgumentException(
                "Reward grant record must belong to the same Player.",
                nameof(rewardGrantRecord));
        }

        if (rewardGrantRecord is not null &&
            rewardGrantRecord.RequestId != requestId)
        {
            throw new ArgumentException(
                "Reward grant record must use the same Request id.",
                nameof(rewardGrantRecord));
        }

        var balanceAfter = checked(balanceBefore + delta);

        if (balanceAfter < 0)
        {
            throw new InvalidOperationException(
                "Currency balance must not become negative.");
        }

        return new CurrencyChangeLog
        {
            PlayerId = playerId,
            CurrencyType = currencyType,
            Delta = delta,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            ReasonType = reasonType,
            SourceId = normalizedSourceId,
            RequestId = requestId,
            RewardGrantRecord = rewardGrantRecord,
            CreatedAt = createdAt
        };
    }
}
