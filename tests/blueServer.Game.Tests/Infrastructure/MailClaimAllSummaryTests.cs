using blueServer.Domain.Entities;
using blueServer.Infrastructure.Mails;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class MailClaimAllSummaryTests
{
    [Fact]
    public void FromResult_AggregatesActualGrantsAndPreservesBatchSnapshot()
    {
        var processedAt = DateTime.UtcNow;
        var result = new MailClaimAllRequestResult(
            MailClaimAllRequestStatus.Processed,
            processedAt,
            1130,
            520,
            true,
            new[]
            {
                Entry(1, MailClaimRequestStatus.Claimed, 100, 15,
                    new MailClaimItemSnapshot(2001, 20, 0, 20),
                    new MailClaimItemSnapshot(1001, 500, 0, 999500)),
                Entry(2, MailClaimRequestStatus.PartiallyClaimed, 30, 5,
                    new MailClaimItemSnapshot(1001, 499, 501, 999999)),
                Entry(3, MailClaimRequestStatus.NoCapacity, 0, 0,
                    new MailClaimItemSnapshot(1001, 0, 100, 999999),
                    new MailClaimItemSnapshot(3001, 0, 10, 999999))
            });

        var summary = MailClaimAllSummary.FromResult(result);

        Assert.Equal(MailClaimAllRequestStatus.Processed, summary.Status);
        Assert.Equal(processedAt, summary.ProcessedAt);
        Assert.Equal(130, summary.GrantedGold);
        Assert.Equal(20, summary.GrantedGem);
        Assert.Equal(1130, summary.CurrentGold);
        Assert.Equal(520, summary.CurrentGem);
        Assert.Equal(1, summary.ClaimedMailCount);
        Assert.Equal(1, summary.PartiallyClaimedMailCount);
        Assert.Equal(1, summary.NoCapacityMailCount);
        Assert.True(summary.HasMore);
        Assert.Equal(new[]
        {
            new MailClaimAllItemSummary(1001, 999),
            new MailClaimAllItemSummary(2001, 20)
        }, summary.Items);
        Assert.Equal(501, result.Entries[1].Claim.Items[0].RemainingQuantity);
    }

    [Theory]
    [InlineData(MailClaimAllRequestStatus.NothingToClaim)]
    [InlineData(MailClaimAllRequestStatus.ConcurrencyConflict)]
    public void FromResult_EmptyOrFailedResult_DoesNotInventGrants(MailClaimAllRequestStatus status)
    {
        var result = new MailClaimAllRequestResult(
            status, null, 0, 0, false, Array.Empty<MailClaimAllEntryResult>());

        var summary = MailClaimAllSummary.FromResult(result);

        Assert.Equal(status, summary.Status);
        Assert.Null(summary.ProcessedAt);
        Assert.Empty(summary.Items);
        Assert.Equal(0, summary.GrantedGold);
        Assert.Equal(0, summary.GrantedGem);
        Assert.Equal(0, summary.ClaimedMailCount);
        Assert.Equal(0, summary.PartiallyClaimedMailCount);
        Assert.Equal(0, summary.NoCapacityMailCount);
        Assert.False(summary.HasMore);
    }

    private static MailClaimAllEntryResult Entry(
        long mailId,
        MailClaimRequestStatus status,
        int gold,
        int gem,
        params MailClaimItemSnapshot[] items)
    {
        return new MailClaimAllEntryResult(
            mailId,
            new MailClaimRequestResult(
                status, null, null, gold, gem, 0, 0, items));
    }
}
