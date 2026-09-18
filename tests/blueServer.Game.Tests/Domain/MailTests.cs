using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class MailTests
{
    [Fact]
    public void Create_AggregatesAttachmentsAndSetsSchedule()
    {
        var sentAt = DateTime.UtcNow;
        var expiresAt = sentAt.AddDays(7);

        var mail = Mail.Create(
            1,
            "  Launch reward  ",
            "  Thank you for playing.  ",
            sentAt,
            expiresAt,
            [
                RewardItem.Create(RewardType.Gold, 100),
                RewardItem.Create(RewardType.Gold, 50),
                RewardItem.Create(RewardType.Gem, 10)
            ],
            MailSourceType.Event,
            "  launch-event:2026  ");

        Assert.Equal(1, mail.PlayerId);
        Assert.Equal(MailSourceType.Event, mail.SourceType);
        Assert.Equal("launch-event:2026", mail.SourceId);
        Assert.Equal("Launch reward", mail.Title);
        Assert.Equal("Thank you for playing.", mail.Body);
        Assert.Equal(sentAt, mail.SentAt);
        Assert.Equal(expiresAt, mail.ExpiresAt);
        Assert.False(mail.IsRead);
        Assert.False(mail.IsClaimed);
        Assert.Collection(
            mail.Attachments.OrderBy(attachment => attachment.Type),
            attachment =>
            {
                Assert.Equal(RewardType.Gold, attachment.Type);
                Assert.Equal(150, attachment.Amount);
            },
            attachment =>
            {
                Assert.Equal(RewardType.Gem, attachment.Type);
                Assert.Equal(10, attachment.Amount);
            });
    }

    [Fact]
    public void HasSameDelivery_ComparesPayloadAndAggregatedAttachments()
    {
        var sentAt = DateTime.UtcNow;
        var first = Mail.Create(
            1,
            "Reward mail",
            "Claim the attached reward.",
            sentAt,
            sentAt.AddDays(1),
            [
                RewardItem.Create(RewardType.Gold, 60),
                RewardItem.Create(RewardType.Gold, 40)
            ],
            MailSourceType.Event,
            "event:100:player:1");
        var same = Mail.Create(
            1,
            "Reward mail",
            "Claim the attached reward.",
            sentAt,
            sentAt.AddDays(1),
            [RewardItem.Create(RewardType.Gold, 100)],
            MailSourceType.Event,
            "event:100:player:1");
        var different = Mail.Create(
            1,
            "Reward mail",
            "Claim the attached reward.",
            sentAt,
            sentAt.AddDays(1),
            [RewardItem.Create(RewardType.Gold, 101)],
            MailSourceType.Event,
            "event:100:player:1");

        Assert.True(first.HasSameDelivery(same));
        Assert.False(first.HasSameDelivery(different));
    }

    [Fact]
    public void Create_AggregatesItemAttachmentsByTemplate()
    {
        var sentAt = DateTime.UtcNow;

        var mail = Mail.Create(
            1,
            "Inventory overflow",
            "Claim the overflow items.",
            sentAt,
            sentAt.AddDays(30),
            sourceType: MailSourceType.System,
            sourceId: "inventory-overflow:1",
            inventoryItemRewards:
            [
                InventoryItemReward.Create(1001, 600),
                InventoryItemReward.Create(1001, 401),
                InventoryItemReward.Create(2001, 3)
            ]);

        Assert.True(mail.HasAttachments);
        Assert.Empty(mail.Attachments);
        Assert.Collection(
            mail.ItemAttachments.OrderBy(item => item.ItemTemplateId),
            item =>
            {
                Assert.Equal(1001, item.ItemTemplateId);
                Assert.Equal(1_001, item.Quantity);
            },
            item =>
            {
                Assert.Equal(2001, item.ItemTemplateId);
                Assert.Equal(3, item.Quantity);
            });
    }

    [Fact]
    public void HasSameDelivery_ComparesItemAttachments()
    {
        var sentAt = DateTime.UtcNow;
        var first = CreateInventoryItemRewardMail(sentAt, 100);
        var same = CreateInventoryItemRewardMail(sentAt, 100);
        var different = CreateInventoryItemRewardMail(sentAt, 101);

        Assert.True(first.HasSameDelivery(same));
        Assert.False(first.HasSameDelivery(different));
    }

    [Fact]
    public void MarkAsRead_PreservesFirstReadTime()
    {
        var sentAt = DateTime.UtcNow;
        var firstReadAt = sentAt.AddMinutes(1);
        var mail = CreateRewardMail(sentAt, sentAt.AddDays(1));

        mail.MarkAsRead(firstReadAt);
        mail.MarkAsRead(firstReadAt.AddMinutes(1));

        Assert.True(mail.IsRead);
        Assert.Equal(firstReadAt, mail.ReadAt);
    }

    [Fact]
    public void Claim_SetsClaimedAndReadTimes()
    {
        var sentAt = DateTime.UtcNow;
        var claimedAt = sentAt.AddMinutes(1);
        var mail = CreateRewardMail(sentAt, sentAt.AddDays(1));

        mail.Claim(claimedAt);

        Assert.True(mail.IsClaimed);
        Assert.True(mail.IsRead);
        Assert.Equal(claimedAt, mail.ClaimedAt);
        Assert.Equal(claimedAt, mail.ReadAt);
    }

    [Fact]
    public void Claim_Throws_WhenMailIsExpired()
    {
        var sentAt = DateTime.UtcNow;
        var expiresAt = sentAt.AddHours(1);
        var mail = CreateRewardMail(sentAt, expiresAt);

        Assert.Throws<InvalidOperationException>(() =>
            mail.Claim(expiresAt));

        Assert.False(mail.IsClaimed);
    }

    [Fact]
    public void Claim_Throws_WhenRewardsWereAlreadyClaimed()
    {
        var sentAt = DateTime.UtcNow;
        var firstClaimedAt = sentAt.AddMinutes(1);
        var mail = CreateRewardMail(sentAt, sentAt.AddDays(1));
        mail.Claim(firstClaimedAt);

        Assert.Throws<InvalidOperationException>(() =>
            mail.Claim(firstClaimedAt.AddMinutes(1)));

        Assert.Equal(firstClaimedAt, mail.ClaimedAt);
    }

    private static Mail CreateRewardMail(
        DateTime sentAt,
        DateTime expiresAt)
    {
        return Mail.Create(
            1,
            "Reward mail",
            "Claim the attached reward.",
            sentAt,
            expiresAt,
            [RewardItem.Create(RewardType.Gold, 100)]);
    }

    private static Mail CreateInventoryItemRewardMail(
        DateTime sentAt,
        int quantity)
    {
        return Mail.Create(
            1,
            "Inventory overflow",
            "Claim the overflow items.",
            sentAt,
            sentAt.AddDays(30),
            sourceType: MailSourceType.System,
            sourceId: "inventory-overflow:1",
            inventoryItemRewards:
            [
                InventoryItemReward.Create(1001, quantity)
            ]);
    }
}
