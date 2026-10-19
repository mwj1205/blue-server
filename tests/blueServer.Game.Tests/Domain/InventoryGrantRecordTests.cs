using blueServer.Domain.Entities;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class InventoryGrantRecordTests
{
    private const string PayloadHash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Create_StoresCompletedItemResults()
    {
        var requestId = Guid.NewGuid();
        var grantedAt = DateTime.UtcNow;

        var record = InventoryGrantRecord.Create(
            7,
            requestId,
            PayloadHash.ToUpperInvariant(),
            grantedAt,
            mailId: 11,
            [
                new InventoryGrantItemSnapshot(
                    1001,
                    RequestedQuantity: 1_200,
                    AppliedQuantity: 999,
                    OverflowQuantity: 201,
                    QuantityAfter: ItemTemplate.InventoryMaxQuantity)
            ]);

        Assert.Equal(7, record.PlayerId);
        Assert.Equal(requestId, record.RequestId);
        Assert.Equal(PayloadHash, record.PayloadHash);
        Assert.Equal(grantedAt, record.GrantedAt);
        Assert.Equal(11, record.MailId);
        Assert.True(record.HasSamePayload(PayloadHash.ToUpperInvariant()));
        Assert.Collection(
            record.Items,
            item =>
            {
                Assert.Equal(1001, item.ItemTemplateId);
                Assert.Equal(1_200, item.RequestedQuantity);
                Assert.Equal(999, item.AppliedQuantity);
                Assert.Equal(201, item.OverflowQuantity);
                Assert.Equal(
                    ItemTemplate.InventoryMaxQuantity,
                    item.QuantityAfter);
            });
    }

    [Fact]
    public void Create_RejectsInconsistentItemQuantities()
    {
        Assert.Throws<ArgumentException>(() => InventoryGrantRecord.Create(
            7,
            Guid.NewGuid(),
            PayloadHash,
            DateTime.UtcNow,
            mailId: null,
            [
                new InventoryGrantItemSnapshot(
                    1001,
                    RequestedQuantity: 100,
                    AppliedQuantity: 80,
                    OverflowQuantity: 30,
                    QuantityAfter: 80)
            ]));
    }

    [Fact]
    public void Create_RejectsDuplicateItemTemplates()
    {
        Assert.Throws<ArgumentException>(() => InventoryGrantRecord.Create(
            7,
            Guid.NewGuid(),
            PayloadHash,
            DateTime.UtcNow,
            mailId: null,
            [
                new InventoryGrantItemSnapshot(1001, 10, 10, 0, 10),
                new InventoryGrantItemSnapshot(1001, 20, 20, 0, 30)
            ]));
    }

    [Fact]
    public void Create_RejectsInvalidPayloadHash()
    {
        Assert.Throws<ArgumentException>(() => InventoryGrantRecord.Create(
            7,
            Guid.NewGuid(),
            "not-a-sha256-hash",
            DateTime.UtcNow,
            mailId: null,
            [new InventoryGrantItemSnapshot(1001, 10, 10, 0, 10)]));
    }
}
