using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class MailItemAttachmentTests
{
    [Fact]
    public void Create_StartsWithAllQuantityRemaining()
    {
        var attachment = MailItemAttachment.Create(1001, 1000);

        Assert.Equal(1000, attachment.Quantity);
        Assert.Equal(0, attachment.ClaimedQuantity);
        Assert.Equal(1000, attachment.RemainingQuantity);
    }

    [Fact]
    public void Claim_AccumulatesPartialClaimsWithoutChangingOriginalQuantity()
    {
        var attachment = MailItemAttachment.Create(1001, 1000);

        attachment.Claim(999);
        Assert.Equal(999, attachment.ClaimedQuantity);
        Assert.Equal(1, attachment.RemainingQuantity);

        attachment.Claim(1);
        Assert.Equal(1000, attachment.ClaimedQuantity);
        Assert.Equal(0, attachment.RemainingQuantity);
        Assert.Equal(1000, attachment.Quantity);
        Assert.Equal(1000, attachment.ToInventoryItemReward().Quantity);
    }

    [Fact]
    public void Claim_ZeroDoesNotChangeProgress()
    {
        var attachment = MailItemAttachment.Create(1001, 1000);
        attachment.Claim(300);

        attachment.Claim(0);

        Assert.Equal(300, attachment.ClaimedQuantity);
        Assert.Equal(700, attachment.RemainingQuantity);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(701)]
    [InlineData(int.MaxValue)]
    public void Claim_RejectsInvalidQuantityWithoutChangingProgress(int quantity)
    {
        var attachment = MailItemAttachment.Create(1001, 1000);
        attachment.Claim(300);

        Assert.Throws<ArgumentOutOfRangeException>(() => attachment.Claim(quantity));

        Assert.Equal(300, attachment.ClaimedQuantity);
        Assert.Equal(700, attachment.RemainingQuantity);
        Assert.Equal(1000, attachment.Quantity);
    }

    [Fact]
    public void Claim_RejectsPositiveQuantityAfterFullClaim()
    {
        var attachment = MailItemAttachment.Create(1001, 1000);
        attachment.Claim(1000);

        Assert.Throws<ArgumentOutOfRangeException>(() => attachment.Claim(1));
        Assert.Equal(1000, attachment.ClaimedQuantity);
        Assert.Equal(0, attachment.RemainingQuantity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Claim_PreservesMailInformation(bool neverExpires)
    {
        var sentAt = new DateTime(2026, 10, 28, 1, 0, 0, DateTimeKind.Utc);
        var mail = Mail.Create(
            1, "Original title", "Original body", sentAt,
            neverExpires ? null : sentAt.AddDays(30),
            sourceType: MailSourceType.Event,
            sourceId: "partial-claim-test",
            inventoryItemRewards: [InventoryItemReward.Create(1001, 1000)]);
        mail.Id = 10;
        mail.MarkAsRead(sentAt.AddMinutes(1));
        var before = (mail.Id, mail.PlayerId, mail.SourceType, mail.SourceId,
            mail.Title, mail.Body, mail.SentAt, mail.ExpiresAt, mail.ReadAt, mail.ClaimedAt);

        var attachment = Assert.Single(mail.ItemAttachments);
        attachment.Mail = mail;
        attachment.Claim(999);

        Assert.Equal(1, attachment.RemainingQuantity);
        Assert.Equal(before, (mail.Id, mail.PlayerId, mail.SourceType, mail.SourceId,
            mail.Title, mail.Body, mail.SentAt, mail.ExpiresAt, mail.ReadAt, mail.ClaimedAt));
        Assert.False(mail.IsClaimed);
    }
}
