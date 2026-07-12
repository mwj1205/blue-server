using blueServer.Domain.Entities;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class StageClearRecordTests
{
    [Fact]
    public void Create_SetsInitialClearCountAndTimestamps()
    {
        var clearedAt = DateTime.UtcNow;

        var record = StageClearRecord.Create(1, 1, clearedAt);

        Assert.Equal(1, record.PlayerId);
        Assert.Equal(1, record.StageTemplateId);
        Assert.Equal(1, record.ClearCount);
        Assert.Equal(clearedAt, record.FirstClearedAt);
        Assert.Equal(clearedAt, record.LastClearedAt);
    }

    [Fact]
    public void RecordClear_IncreasesClearCountAndUpdatesLastClearedAt()
    {
        var firstClearedAt = DateTime.UtcNow;
        var secondClearedAt = firstClearedAt.AddMinutes(1);
        var record = StageClearRecord.Create(1, 1, firstClearedAt);

        record.RecordClear(secondClearedAt);

        Assert.Equal(2, record.ClearCount);
        Assert.Equal(firstClearedAt, record.FirstClearedAt);
        Assert.Equal(secondClearedAt, record.LastClearedAt);
    }
}
