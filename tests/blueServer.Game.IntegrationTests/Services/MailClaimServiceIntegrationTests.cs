using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Mails;
using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class MailClaimServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task ClaimAsync_GrantsRewardsOnceAndChecksOwnership()
    {
        var options = CreateDbContextOptions();
        var sentAt = new DateTime(
            2026,
            8,
            27,
            8,
            0,
            0,
            DateTimeKind.Utc);
        var claimedAt = sentAt.AddHours(1);
        long ownerId;
        long otherPlayerId;
        long mailId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var owner = Player.Create(
                $"mail-claim-owner-{Guid.NewGuid():N}",
                "integration-test");
            var otherPlayer = Player.Create(
                $"mail-claim-other-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.AddRange(owner, otherPlayer);
            await arrangeDb.SaveChangesAsync();

            var mail = Mail.Create(
                owner.Id,
                "Claim reward test",
                "Gold and Gem must be granted once.",
                sentAt,
                sentAt.AddDays(1),
                [
                    RewardItem.Create(RewardType.Gold, 120),
                    RewardItem.Create(RewardType.Gem, 15)
                ]);
            arrangeDb.Mails.Add(mail);
            await arrangeDb.SaveChangesAsync();

            ownerId = owner.Id;
            otherPlayerId = otherPlayer.Id;
            mailId = mail.Id;
        }

        await using (var otherPlayerDb = new GameDbContext(options))
        {
            var service = CreateService(otherPlayerDb);
            var result = await service.ClaimAsync(
                otherPlayerId,
                mailId,
                claimedAt);

            Assert.Equal(MailClaimStatus.NotFound, result.Status);
        }

        await using (var claimDb = new GameDbContext(options))
        {
            var service = CreateService(claimDb);
            var result = await service.ClaimAsync(
                ownerId,
                mailId,
                claimedAt);

            Assert.Equal(MailClaimStatus.Claimed, result.Status);
            Assert.Equal(claimedAt, result.ClaimedAt);
            Assert.Equal(Player.InitialGold + 120, result.CurrentGold);
            Assert.Equal(Player.InitialGem + 15, result.CurrentGem);
        }

        await using (var retryDb = new GameDbContext(options))
        {
            var service = CreateService(retryDb);
            var result = await service.ClaimAsync(
                ownerId,
                mailId,
                claimedAt.AddMinutes(1));

            Assert.Equal(MailClaimStatus.AlreadyClaimed, result.Status);
            Assert.Equal(claimedAt, result.ClaimedAt);
            Assert.Equal(Player.InitialGold + 120, result.CurrentGold);
            Assert.Equal(Player.InitialGem + 15, result.CurrentGem);
        }

        await using (var assertDb = new GameDbContext(options))
        {
            var player = await assertDb.Players
                .AsNoTracking()
                .SingleAsync(player => player.Id == ownerId);
            var mail = await assertDb.Mails
                .AsNoTracking()
                .SingleAsync(mail => mail.Id == mailId);
            var grant = await assertDb.RewardGrantRecords
                .AsNoTracking()
                .Include(record => record.Items)
                .SingleAsync(record =>
                    record.PlayerId == ownerId &&
                    record.Reason == $"Mail reward {mailId}");

            Assert.Equal(Player.InitialGold + 120, player.Gold);
            Assert.Equal(Player.InitialGem + 15, player.Gem);
            Assert.Equal(claimedAt, mail.ReadAt);
            Assert.Equal(claimedAt, mail.ClaimedAt);
            Assert.Collection(
                grant.Items.OrderBy(item => item.Type),
                item =>
                {
                    Assert.Equal(RewardType.Gold, item.Type);
                    Assert.Equal(120, item.Amount);
                },
                item =>
                {
                    Assert.Equal(RewardType.Gem, item.Type);
                    Assert.Equal(15, item.Amount);
                });
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task ClaimAsync_ConcurrentRequestsGrantRewardsOnce()
    {
        var options = CreateDbContextOptions();
        var sentAt = new DateTime(
            2026,
            8,
            27,
            9,
            0,
            0,
            DateTimeKind.Utc);
        long playerId;
        long mailId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var player = Player.Create(
                $"mail-claim-concurrent-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.Add(player);
            await arrangeDb.SaveChangesAsync();

            var mail = Mail.Create(
                player.Id,
                "Concurrent claim test",
                "Concurrent requests must grant rewards once.",
                sentAt,
                sentAt.AddDays(1),
                [RewardItem.Create(RewardType.Gold, 200)]);
            arrangeDb.Mails.Add(mail);
            await arrangeDb.SaveChangesAsync();

            playerId = player.Id;
            mailId = mail.Id;
        }

        var results = await Task.WhenAll(
            ClaimWithNewDbContextAsync(
                options,
                playerId,
                mailId,
                sentAt.AddHours(1)),
            ClaimWithNewDbContextAsync(
                options,
                playerId,
                mailId,
                sentAt.AddHours(1).AddMilliseconds(1)));

        Assert.Single(
            results,
            result => result.Status == MailClaimStatus.Claimed);
        Assert.Single(
            results,
            result => result.Status == MailClaimStatus.AlreadyClaimed);

        await using var assertDb = new GameDbContext(options);
        var currentGold = await assertDb.Players
            .AsNoTracking()
            .Where(player => player.Id == playerId)
            .Select(player => player.Gold)
            .SingleAsync();
        var grantCount = await assertDb.RewardGrantRecords
            .AsNoTracking()
            .CountAsync(record =>
                record.PlayerId == playerId &&
                record.Reason == $"Mail reward {mailId}");

        Assert.Equal(Player.InitialGold + 200, currentGold);
        Assert.Equal(1, grantCount);
    }

    private static DbContextOptions<GameDbContext> CreateDbContextOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;

        return new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }

    private static MailClaimService CreateService(GameDbContext db)
    {
        return new MailClaimService(
            db,
            new RewardGrantService(db));
    }

    private static async Task<MailClaimResult> ClaimWithNewDbContextAsync(
        DbContextOptions<GameDbContext> options,
        long playerId,
        long mailId,
        DateTime claimedAt)
    {
        await using var db = new GameDbContext(options);
        var service = CreateService(db);

        return await service.ClaimAsync(
            playerId,
            mailId,
            claimedAt);
    }
}
