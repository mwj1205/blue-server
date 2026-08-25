using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class RewardGrantRecordTests
{
    [Fact]
    public void Create_SetsIdempotencyFields()
    {
        var requestId = Guid.NewGuid();
        var grantedAt = DateTime.UtcNow;

        var record = RewardGrantRecord.Create(
            1,
            requestId,
            "Stage clear",
            grantedAt,
            RewardBundle.Create(
                RewardItem.Create(RewardType.Gold, 100),
                RewardItem.Create(RewardType.Gem, 10)));

        Assert.Equal(1, record.PlayerId);
        Assert.Equal(requestId, record.RequestId);
        Assert.Equal("Stage clear", record.Reason);
        Assert.Equal(grantedAt, record.GrantedAt);
        Assert.Collection(
            record.Items.OrderBy(item => item.Type),
            item =>
            {
                Assert.Equal(RewardType.Gold, item.Type);
                Assert.Equal(100, item.Amount);
            },
            item =>
            {
                Assert.Equal(RewardType.Gem, item.Type);
                Assert.Equal(10, item.Amount);
            });
    }

    [Fact]
    public void Create_AggregatesSameRewardType()
    {
        var record = RewardGrantRecord.Create(
            1,
            Guid.NewGuid(),
            "Mail claim",
            DateTime.UtcNow,
            RewardBundle.Create(
                RewardItem.Create(RewardType.Gold, 100),
                RewardItem.Create(RewardType.Gold, 50)));

        var item = Assert.Single(record.Items);
        Assert.Equal(RewardType.Gold, item.Type);
        Assert.Equal(150, item.Amount);
    }

    [Fact]
    public void HasSameGrant_ReturnsFalse_WhenRewardPayloadDiffers()
    {
        var record = RewardGrantRecord.Create(
            1,
            Guid.NewGuid(),
            "Mail claim",
            DateTime.UtcNow,
            RewardBundle.Create(
                RewardItem.Create(RewardType.Gold, 100)));

        var isSame = record.HasSameGrant(
            "Mail claim",
            RewardBundle.Create(
                RewardItem.Create(RewardType.Gold, 101)));

        Assert.False(isSame);
    }
}
