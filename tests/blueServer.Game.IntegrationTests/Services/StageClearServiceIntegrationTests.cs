using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;
using blueServer.Game.Services;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Currencies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class StageClearServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task ClearAsync_PersistsClearRecordRewardsAndCurrencyHistory()
    {
        var options = CreateOptions();
        long playerId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var template = new CharacterTemplate
            {
                Name = $"stage-clear-template-{Guid.NewGuid():N}",
                Rarity = 3,
                Role = "Dealer"
            };
            var player = Player.Create(
                $"stage-clear-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.AddRange(template, player);
            await arrangeDb.SaveChangesAsync();

            var ownedCharacter = OwnedCharacter.Create(player.Id, template);
            arrangeDb.OwnedCharacters.Add(ownedCharacter);
            await arrangeDb.SaveChangesAsync();

            var party = Party.Create(player.Id, Party.MinPartyNo);
            party.SetSlot(PartySlot.MinSlotIndex, ownedCharacter);
            arrangeDb.Parties.Add(party);
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;
        }

        StageClearResult result;

        await using (var clearDb = new GameDbContext(options))
        {
            var service = new StageClearService(
                clearDb,
                new CurrencyChangeService(clearDb));
            result = await service.ClearAsync(
                playerId,
                stageTemplateId: 1,
                partyNo: Party.MinPartyNo,
                CancellationToken.None);
        }

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ClearCount);

        await using (var assertDb = new GameDbContext(options))
        {
            var player = await assertDb.Players
                .AsNoTracking()
                .SingleAsync(player => player.Id == playerId);
            var record = await assertDb.StageClearRecords
                .AsNoTracking()
                .SingleAsync(record => record.PlayerId == playerId);
            var changes = await assertDb.CurrencyChangeLogs
                .AsNoTracking()
                .Where(change => change.PlayerId == playerId)
                .OrderBy(change => change.CurrencyType)
                .ToArrayAsync();

            Assert.Equal(1, record.StageTemplateId);
            Assert.Equal(1, record.ClearCount);
            Assert.Equal(Player.InitialGold + result.RewardGold, player.Gold);
            Assert.Equal(Player.InitialGem + result.RewardGem, player.Gem);
            Assert.Collection(
                changes,
                change => AssertCurrencyChange(
                    change,
                    CurrencyType.Gold,
                    result.RewardGold,
                    Player.InitialGold,
                    player.Gold),
                change => AssertCurrencyChange(
                    change,
                    CurrencyType.Gem,
                    result.RewardGem,
                    Player.InitialGem,
                    player.Gem));
            Assert.Equal(changes[0].RequestId, changes[1].RequestId);
            Assert.Equal(changes[0].SourceId, changes[1].SourceId);
            Assert.StartsWith("stage-clear:1:", changes[0].SourceId);
        }
    }

    private static void AssertCurrencyChange(
        CurrencyChangeLog change,
        CurrencyType currencyType,
        int delta,
        int balanceBefore,
        int balanceAfter)
    {
        Assert.Equal(currencyType, change.CurrencyType);
        Assert.Equal(delta, change.Delta);
        Assert.Equal(balanceBefore, change.BalanceBefore);
        Assert.Equal(balanceAfter, change.BalanceAfter);
        Assert.Equal(
            CurrencyChangeReasonType.StageClearReward,
            change.ReasonType);
        Assert.NotEqual(Guid.Empty, change.RequestId);
        Assert.Null(change.RewardGrantRecordId);
    }

    private static DbContextOptions<GameDbContext> CreateOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;

        return new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }
}
