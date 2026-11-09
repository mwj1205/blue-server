using blueServer.Domain.Entities;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class MailClaimAllRecordTests
{
    private static readonly DateTime ProcessedAt = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_PreservesProcessingOrderAndReusesMailClaimRecords()
    {
        var requestId = Guid.NewGuid();
        var first = CreateClaim(20, grantedQuantity: 1);
        var second = CreateClaim(10, grantedQuantity: 0);
        var claims = new List<MailClaimRecord> { first, second };

        var record = MailClaimAllRecord.Create(
            1,
            requestId,
            ProcessedAt,
            1100,
            500,
            hasMore: true,
            claims);
        claims.Clear();

        Assert.Equal(requestId, record.RequestId);
        Assert.Equal(ProcessedAt, record.ProcessedAt);
        Assert.Equal(1100, record.CurrentGold);
        Assert.Equal(500, record.CurrentGem);
        Assert.True(record.HasMore);
        Assert.Collection(
            record.Entries,
            entry =>
            {
                Assert.Equal(0, entry.Position);
                Assert.Same(first, entry.MailClaimRecord);
            },
            entry =>
            {
                Assert.Equal(1, entry.Position);
                Assert.Same(second, entry.MailClaimRecord);
                Assert.Equal(0, Assert.Single(entry.MailClaimRecord.Items).GrantedQuantity);
            });
    }

    [Fact]
    public void Create_AllowsEmptyBatchResultForRetry()
    {
        var record = Create([]);

        Assert.Empty(record.Entries);
        Assert.False(record.HasMore);
        Assert.Equal(1000, record.CurrentGold);
    }

    [Fact]
    public void Create_RejectsDuplicateMailIds()
    {
        Assert.Throws<ArgumentException>(() => Create([CreateClaim(10), CreateClaim(10)]));
    }

    [Fact]
    public void Create_RejectsSharedPerMailRequestId()
    {
        var operationId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => Create(
            [CreateClaim(10, requestId: operationId), CreateClaim(20, requestId: operationId)]));
    }

    [Fact]
    public void Create_RejectsUsingBatchRequestIdAsPerMailRequestId()
    {
        var requestId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => Create(
            [CreateClaim(10, requestId: requestId)],
            requestId));
    }

    [Fact]
    public void Create_RejectsClaimFromDifferentPlayer()
    {
        Assert.Throws<ArgumentException>(() => Create([CreateClaim(10, playerId: 2)]));
    }

    [Fact]
    public void Create_RejectsClaimFromDifferentProcessingTime()
    {
        Assert.Throws<ArgumentException>(() => Create(
            [CreateClaim(10, processedAt: ProcessedAt.AddSeconds(-1))]));
    }

    [Theory]
    [InlineData(MailClaimAllRecord.MaxMailCount, true)]
    [InlineData(MailClaimAllRecord.MaxMailCount + 1, false)]
    public void Create_EnforcesMailCountLimit(int count, bool isAllowed)
    {
        var claims = Enumerable.Range(1, count).Select(id => CreateClaim(id));

        if (isAllowed)
        {
            Assert.Equal(count, Create(claims).Entries.Count);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => Create(claims));
        }
    }

    private static MailClaimAllRecord Create(
        IEnumerable<MailClaimRecord> claims,
        Guid? requestId = null)
    {
        return MailClaimAllRecord.Create(
            1,
            requestId ?? Guid.NewGuid(),
            ProcessedAt,
            1000,
            500,
            hasMore: false,
            claims);
    }

    private static MailClaimRecord CreateClaim(
        long mailId,
        long playerId = 1,
        Guid? requestId = null,
        DateTime? processedAt = null,
        int grantedQuantity = 0)
    {
        return MailClaimRecord.Create(
            playerId,
            mailId,
            requestId ?? Guid.NewGuid(),
            processedAt ?? ProcessedAt,
            mailClaimedAt: null,
            grantedGold: 0,
            grantedGem: 0,
            currentGold: 1000,
            currentGem: 500,
            [new MailClaimItemSnapshot(1001, grantedQuantity, 1, 999999)]);
    }
}
