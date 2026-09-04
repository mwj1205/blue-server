using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class CurrencyChangeLogTests
{
    [Fact]
    public void Create_CalculatesBalanceAfterAndNormalizesSourceId()
    {
        var requestId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        var change = CurrencyChangeLog.Create(
            1,
            CurrencyType.Gold,
            -300,
            1_000,
            CurrencyChangeReasonType.EquipmentUpgradeCost,
            "  equipment-upgrade:42  ",
            requestId,
            createdAt);

        Assert.Equal(1, change.PlayerId);
        Assert.Equal(CurrencyType.Gold, change.CurrencyType);
        Assert.Equal(-300, change.Delta);
        Assert.Equal(1_000, change.BalanceBefore);
        Assert.Equal(700, change.BalanceAfter);
        Assert.Equal(
            CurrencyChangeReasonType.EquipmentUpgradeCost,
            change.ReasonType);
        Assert.Equal("equipment-upgrade:42", change.SourceId);
        Assert.Equal(requestId, change.RequestId);
        Assert.Equal(createdAt, change.CreatedAt);
        Assert.Null(change.RewardGrantRecordId);
    }

    [Fact]
    public void Create_RejectsNegativeResultBalance()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CurrencyChangeLog.Create(
                1,
                CurrencyType.Gem,
                -501,
                500,
                CurrencyChangeReasonType.GachaCost,
                "gacha:request-1",
                Guid.NewGuid(),
                DateTime.UtcNow));
    }

    [Fact]
    public void Create_RejectsZeroDelta()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CurrencyChangeLog.Create(
                1,
                CurrencyType.Gold,
                0,
                1_000,
                CurrencyChangeReasonType.AdminAdjustment,
                "admin-adjustment:1",
                Guid.NewGuid(),
                DateTime.UtcNow));
    }

    [Fact]
    public void Create_RejectsNonUtcTime()
    {
        Assert.Throws<ArgumentException>(() =>
            CurrencyChangeLog.Create(
                1,
                CurrencyType.Gold,
                100,
                1_000,
                CurrencyChangeReasonType.RewardGrant,
                "reward:1",
                Guid.NewGuid(),
                DateTime.Now));
    }

    [Fact]
    public void Create_LinksUnsavedRewardGrantRecordByEntityReference()
    {
        var requestId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var rewardGrantRecord = RewardGrantRecord.Create(
            1,
            requestId,
            "Mail reward",
            createdAt,
            RewardBundle.Create(
                RewardItem.Create(RewardType.Gold, 100)));

        var change = CurrencyChangeLog.Create(
            1,
            CurrencyType.Gold,
            100,
            1_000,
            CurrencyChangeReasonType.RewardGrant,
            "mail:42",
            requestId,
            createdAt,
            rewardGrantRecord);

        Assert.Same(rewardGrantRecord, change.RewardGrantRecord);
        Assert.Null(change.RewardGrantRecordId);
    }

    [Fact]
    public void Create_RejectsRewardGrantRecordForAnotherPlayer()
    {
        var requestId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var rewardGrantRecord = RewardGrantRecord.Create(
            2,
            requestId,
            "Mail reward",
            createdAt,
            RewardBundle.Create(
                RewardItem.Create(RewardType.Gold, 100)));

        Assert.Throws<ArgumentException>(() =>
            CurrencyChangeLog.Create(
                1,
                CurrencyType.Gold,
                100,
                1_000,
                CurrencyChangeReasonType.RewardGrant,
                "mail:42",
                requestId,
                createdAt,
                rewardGrantRecord));
    }
}
