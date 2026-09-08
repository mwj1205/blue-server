using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;
using blueServer.Game.Repositories;
using blueServer.Game.Services;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Currencies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class CharacterGachaServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task DrawAsync_PersistsCharacterGemCostAndCurrencyHistory()
    {
        var options = CreateOptions();
        long playerId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            arrangeDb.CharacterTemplates.Add(new CharacterTemplate
            {
                Name = $"gacha-template-{Guid.NewGuid():N}",
                Rarity = 3,
                Role = "Dealer"
            });

            var player = Player.Create(
                $"gacha-success-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.Add(player);
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;
        }

        CharacterGachaResult result;

        await using (var gachaDb = new GameDbContext(options))
        {
            var service = CreateService(gachaDb);
            result = await service.DrawAsync(playerId, CancellationToken.None);
        }

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Player.InitialGem - CharacterGachaService.GachaCost,
            result.RemainingGem);

        await using (var assertDb = new GameDbContext(options))
        {
            var player = await assertDb.Players
                .AsNoTracking()
                .SingleAsync(player => player.Id == playerId);
            var ownedCharacter = await assertDb.OwnedCharacters
                .AsNoTracking()
                .SingleAsync(character => character.PlayerId == playerId);
            var currencyChange = await assertDb.CurrencyChangeLogs
                .AsNoTracking()
                .SingleAsync(change => change.PlayerId == playerId);

            Assert.Equal(result.OwnedCharacterId, ownedCharacter.Id);
            Assert.Equal(
                result.CharacterTemplateId,
                ownedCharacter.CharacterTemplateId);
            Assert.Equal(
                Player.InitialGem - CharacterGachaService.GachaCost,
                player.Gem);
            Assert.Equal(CurrencyType.Gem, currencyChange.CurrencyType);
            Assert.Equal(
                -CharacterGachaService.GachaCost,
                currencyChange.Delta);
            Assert.Equal(Player.InitialGem, currencyChange.BalanceBefore);
            Assert.Equal(player.Gem, currencyChange.BalanceAfter);
            Assert.Equal(
                CurrencyChangeReasonType.GachaCost,
                currencyChange.ReasonType);
            Assert.StartsWith("gacha:", currencyChange.SourceId);
            Assert.NotEqual(Guid.Empty, currencyChange.RequestId);
            Assert.Null(currencyChange.RewardGrantRecordId);
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task DrawAsync_DoesNotPersistCharacterOrHistory_WhenGemIsInsufficient()
    {
        var options = CreateOptions();
        long playerId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            if (!await arrangeDb.CharacterTemplates.AnyAsync())
            {
                arrangeDb.CharacterTemplates.Add(new CharacterTemplate
                {
                    Name = $"gacha-template-{Guid.NewGuid():N}",
                    Rarity = 3,
                    Role = "Dealer"
                });
            }

            var player = Player.Create(
                $"gacha-insufficient-{Guid.NewGuid():N}",
                "integration-test");
            player.Gem = CharacterGachaService.GachaCost - 1;
            arrangeDb.Players.Add(player);
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;
        }

        CharacterGachaResult result;

        await using (var gachaDb = new GameDbContext(options))
        {
            var service = CreateService(gachaDb);
            result = await service.DrawAsync(playerId, CancellationToken.None);
        }

        Assert.False(result.IsSuccess);
        Assert.Equal("Not enough gems", result.Message);
        Assert.Equal(
            CharacterGachaService.GachaCost - 1,
            result.RemainingGem);

        await using (var assertDb = new GameDbContext(options))
        {
            var player = await assertDb.Players
                .AsNoTracking()
                .SingleAsync(player => player.Id == playerId);

            Assert.Equal(
                CharacterGachaService.GachaCost - 1,
                player.Gem);
            Assert.False(await assertDb.OwnedCharacters
                .AnyAsync(character => character.PlayerId == playerId));
            Assert.False(await assertDb.CurrencyChangeLogs
                .AnyAsync(change => change.PlayerId == playerId));
        }
    }

    private static CharacterGachaService CreateService(GameDbContext db)
    {
        return new CharacterGachaService(
            db,
            new PlayerRepository(db),
            new OwnedCharacterRepository(db),
            new CharacterTemplateRepository(db),
            new CurrencyChangeService(db));
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
