using blueServer.Domain.Entities;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class MailClaimRecordTests
{
    private static readonly DateTime ProcessedAt = new(2026, 10, 28, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_PreservesPartialClaimSnapshot()
    {
        var requestId = Guid.NewGuid();
        var record = MailClaimRecord.Create(
            1, 10, requestId, ProcessedAt, null,
            100, 10, 1100, 510,
            [new MailClaimItemSnapshot(1001, 999, 1, 999999)]);

        Assert.Equal(requestId, record.RequestId);
        Assert.Equal(1, record.PlayerId);
        Assert.Equal(10, record.MailId);
        Assert.Equal(ProcessedAt, record.ProcessedAt);
        Assert.Null(record.MailClaimedAt);
        Assert.Equal(100, record.GrantedGold);
        Assert.Equal(10, record.GrantedGem);
        Assert.Equal(1100, record.CurrentGold);
        Assert.Equal(510, record.CurrentGem);
        var item = Assert.Single(record.Items);
        Assert.Equal(1001, item.ItemTemplateId);
        Assert.Equal(999, item.GrantedQuantity);
        Assert.Equal(1, item.RemainingQuantity);
        Assert.Equal(999999, item.QuantityAfter);
    }

    [Fact]
    public void Create_StoresZeroGrantResultWithoutCompletingMail()
    {
        var record = Create([new MailClaimItemSnapshot(1001, 0, 1000, 999999)]);

        Assert.Equal(0, Assert.Single(record.Items).GrantedQuantity);
        Assert.Null(record.MailClaimedAt);
        Assert.Equal(0, record.GrantedGold);
    }

    [Fact]
    public void Create_AllowsCompletedCurrencyOnlyClaim()
    {
        var record = MailClaimRecord.Create(
            1, 10, Guid.NewGuid(), ProcessedAt, ProcessedAt,
            100, 0, 1100, 500, []);

        Assert.Equal(ProcessedAt, record.MailClaimedAt);
        Assert.Empty(record.Items);
    }

    [Fact]
    public void Create_RejectsCompletedClaimWithRemainingItems()
    {
        Assert.Throws<ArgumentException>(() => Create(
            [new MailClaimItemSnapshot(1001, 999, 1, 999999)], ProcessedAt));
    }

    [Fact]
    public void Create_RejectsDuplicateItemTemplates()
    {
        var item = new MailClaimItemSnapshot(1001, 1, 0, 1);
        Assert.Throws<ArgumentException>(() => Create([item, item]));
    }

    [Theory]
    [InlineData(-1, 1, 0)]
    [InlineData(1, -1, 1)]
    [InlineData(2, 0, 1)]
    [InlineData(1, 0, 1000000)]
    public void Create_RejectsInvalidItemSnapshot(int granted, int remaining, int quantityAfter)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(
            [new MailClaimItemSnapshot(1001, granted, remaining, quantityAfter)]));
    }

    [Fact]
    public void Create_RejectsMissingRequestIdentity()
    {
        Assert.Throws<ArgumentException>(() => MailClaimRecord.Create(
            1, 10, Guid.Empty, ProcessedAt, null, 0, 0, 1000, 500, []));
    }

    [Fact]
    public void Create_RejectsCompletionLaterThanProcessing()
    {
        Assert.Throws<ArgumentException>(() => Create([], ProcessedAt.AddSeconds(1)));
    }

    private static MailClaimRecord Create(
        IEnumerable<MailClaimItemSnapshot> items, DateTime? mailClaimedAt = null) =>
        MailClaimRecord.Create(1, 10, Guid.NewGuid(), ProcessedAt, mailClaimedAt,
            0, 0, 1000, 500, items);
}
