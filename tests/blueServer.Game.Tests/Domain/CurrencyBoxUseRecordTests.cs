using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class CurrencyBoxUseRecordTests
{
    private static readonly DateTime ProcessedAt = new(2026, 11, 18, 1, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(CurrencyType.Gold, 1227, 500)]
    [InlineData(CurrencyType.Gem, 1000, 727)]
    public void Create_PreservesConfirmedRewardAndRequestIdentity(CurrencyType currency, int gold, int gem)
    {
        var requestId = Guid.NewGuid();
        var rewardId = Guid.NewGuid();
        var record = CurrencyBoxUseRecord.Create(
            1, requestId, rewardId, 3103, 3, 7, currency, 227, gold, gem, ProcessedAt);

        Assert.Equal(1, record.PlayerId);
        Assert.Equal(requestId, record.RequestId);
        Assert.Equal(rewardId, record.RewardRequestId);
        Assert.Equal(3103, record.ItemTemplateId);
        Assert.Equal(3, record.UsedQuantity);
        Assert.Equal(7, record.QuantityAfter);
        Assert.Equal(currency, record.CurrencyType);
        Assert.Equal(227, record.GrantedAmount);
        Assert.Equal(gold, record.CurrentGold);
        Assert.Equal(gem, record.CurrentGem);
        Assert.Equal(ProcessedAt, record.ProcessedAt);
        Assert.True(record.HasSameRequest(3103, 3));
        Assert.False(record.HasSameRequest(3104, 3));
        Assert.False(record.HasSameRequest(3103, 4));
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(3101, 0, 0)]
    [InlineData(3101, 1, -1)]
    [InlineData(3101, 1, 999999)]
    [InlineData(3101, int.MaxValue, int.MaxValue)]
    public void Create_RejectsInvalidConsumptionSnapshot(int itemId, int used, int remaining)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CurrencyBoxUseRecord.Create(
            1, Guid.NewGuid(), Guid.NewGuid(), itemId, used, remaining,
            CurrencyType.Gold, 100, 1100, 500, ProcessedAt));
    }

    [Fact]
    public void Create_RejectsMissingIdentityAndNonUtcTime()
    {
        Assert.Throws<ArgumentException>(() => CurrencyBoxUseRecord.Create(
            1, Guid.Empty, Guid.NewGuid(), 3101, 1, 0, CurrencyType.Gold, 100, 1100, 500, ProcessedAt));
        Assert.Throws<ArgumentException>(() => CurrencyBoxUseRecord.Create(
            1, Guid.NewGuid(), Guid.Empty, 3101, 1, 0, CurrencyType.Gold, 100, 1100, 500, ProcessedAt));
        Assert.Throws<ArgumentException>(() => CurrencyBoxUseRecord.Create(
            1, Guid.NewGuid(), Guid.NewGuid(), 3101, 1, 0, CurrencyType.Gold, 100, 1100, 500,
            DateTime.SpecifyKind(ProcessedAt, DateTimeKind.Unspecified)));
    }

    [Theory]
    [InlineData(0, 1100, 500)]
    [InlineData(100, 99, 500)]
    [InlineData(100, 1100, -1)]
    public void Create_RejectsInvalidRewardOrBalance(int amount, int gold, int gem)
    {
        Assert.ThrowsAny<ArgumentException>(() => CurrencyBoxUseRecord.Create(
            1, Guid.NewGuid(), Guid.NewGuid(), 3101, 1, 0, CurrencyType.Gold, amount, gold, gem, ProcessedAt));
    }
}
