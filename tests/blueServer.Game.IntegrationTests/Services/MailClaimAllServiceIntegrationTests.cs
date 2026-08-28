using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Mails;
using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class MailClaimAllServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task ClaimAllAsync_ClaimsEligibleMailsAndSkipsOthers()
    {
        var options = CreateDbContextOptions();
        var claimedAt = new DateTime(
            2026,
            8,
            28,
            1,
            0,
            0,
            DateTimeKind.Utc);
        var sentAt = claimedAt.AddHours(-2);
        long playerId;
        long otherPlayerId;
        long firstEligibleMailId;
        long secondEligibleMailId;
        long expiredMailId;
        long emptyMailId;
        long alreadyClaimedMailId;
        long otherPlayerMailId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var player = Player.Create(
                $"mail-claim-all-{Guid.NewGuid():N}",
                "integration-test");
            var otherPlayer = Player.Create(
                $"mail-claim-all-other-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.AddRange(player, otherPlayer);
            await arrangeDb.SaveChangesAsync();

            var firstEligibleMail = Mail.Create(
                player.Id,
                "First eligible Mail",
                "This Mail must be claimed.",
                sentAt,
                claimedAt.AddDays(1),
                [
                    RewardItem.Create(RewardType.Gold, 100),
                    RewardItem.Create(RewardType.Gem, 10)
                ]);
            var secondEligibleMail = Mail.Create(
                player.Id,
                "Second eligible Mail",
                "This Mail must also be claimed.",
                sentAt.AddMinutes(1),
                claimedAt.AddDays(1),
                [RewardItem.Create(RewardType.Gold, 50)]);
            var expiredMail = Mail.Create(
                player.Id,
                "Expired Mail",
                "This Mail must be skipped.",
                sentAt,
                claimedAt.AddHours(-1),
                [RewardItem.Create(RewardType.Gold, 999)]);
            var emptyMail = Mail.Create(
                player.Id,
                "Empty Mail",
                "This Mail has no rewards.",
                sentAt);
            var alreadyClaimedMail = Mail.Create(
                player.Id,
                "Already claimed Mail",
                "This Mail must not be granted again.",
                sentAt,
                claimedAt.AddDays(1),
                [RewardItem.Create(RewardType.Gem, 999)]);
            alreadyClaimedMail.Claim(claimedAt.AddHours(-1));
            var otherPlayerMail = Mail.Create(
                otherPlayer.Id,
                "Other Player Mail",
                "This Mail belongs to another Player.",
                sentAt,
                claimedAt.AddDays(1),
                [RewardItem.Create(RewardType.Gold, 999)]);

            arrangeDb.Mails.AddRange(
                firstEligibleMail,
                secondEligibleMail,
                expiredMail,
                emptyMail,
                alreadyClaimedMail,
                otherPlayerMail);
            await arrangeDb.SaveChangesAsync();

            playerId = player.Id;
            otherPlayerId = otherPlayer.Id;
            firstEligibleMailId = firstEligibleMail.Id;
            secondEligibleMailId = secondEligibleMail.Id;
            expiredMailId = expiredMail.Id;
            emptyMailId = emptyMail.Id;
            alreadyClaimedMailId = alreadyClaimedMail.Id;
            otherPlayerMailId = otherPlayerMail.Id;
        }

        await using (var claimDb = new GameDbContext(options))
        {
            var service = CreateService(claimDb);
            var result = await service.ClaimAllAsync(
                playerId,
                claimedAt);

            Assert.Equal(MailClaimAllStatus.Claimed, result.Status);
            Assert.Equal(2, result.ClaimedMailCount);
            Assert.Equal(150, result.GrantedGold);
            Assert.Equal(10, result.GrantedGem);
            Assert.Equal(Player.InitialGold + 150, result.CurrentGold);
            Assert.Equal(Player.InitialGem + 10, result.CurrentGem);
            Assert.False(result.HasMore);
        }

        // 같은 요청 재시도에서 추가 지급 없이 현재 재화 반환
        await using (var retryDb = new GameDbContext(options))
        {
            var service = CreateService(retryDb);
            var result = await service.ClaimAllAsync(
                playerId,
                claimedAt.AddMinutes(1));

            Assert.Equal(MailClaimAllStatus.NothingToClaim, result.Status);
            Assert.Equal(0, result.ClaimedMailCount);
            Assert.Equal(Player.InitialGold + 150, result.CurrentGold);
            Assert.Equal(Player.InitialGem + 10, result.CurrentGem);
        }

        await using (var assertDb = new GameDbContext(options))
        {
            var player = await assertDb.Players
                .AsNoTracking()
                .SingleAsync(player => player.Id == playerId);
            var otherPlayer = await assertDb.Players
                .AsNoTracking()
                .SingleAsync(player => player.Id == otherPlayerId);
            var mails = await assertDb.Mails
                .AsNoTracking()
                .Where(mail =>
                    mail.Id == firstEligibleMailId ||
                    mail.Id == secondEligibleMailId ||
                    mail.Id == expiredMailId ||
                    mail.Id == emptyMailId ||
                    mail.Id == alreadyClaimedMailId ||
                    mail.Id == otherPlayerMailId)
                .ToDictionaryAsync(mail => mail.Id);
            var grantReasons = await assertDb.RewardGrantRecords
                .AsNoTracking()
                .Where(record => record.PlayerId == playerId)
                .Select(record => record.Reason)
                .ToArrayAsync();

            Assert.Equal(Player.InitialGold + 150, player.Gold);
            Assert.Equal(Player.InitialGem + 10, player.Gem);
            Assert.Equal(Player.InitialGold, otherPlayer.Gold);
            Assert.Equal(Player.InitialGem, otherPlayer.Gem);
            Assert.Equal(claimedAt, mails[firstEligibleMailId].ReadAt);
            Assert.Equal(claimedAt, mails[firstEligibleMailId].ClaimedAt);
            Assert.Equal(claimedAt, mails[secondEligibleMailId].ReadAt);
            Assert.Equal(claimedAt, mails[secondEligibleMailId].ClaimedAt);
            Assert.Null(mails[expiredMailId].ClaimedAt);
            Assert.Null(mails[emptyMailId].ClaimedAt);
            Assert.Equal(
                claimedAt.AddHours(-1),
                mails[alreadyClaimedMailId].ClaimedAt);
            Assert.Null(mails[otherPlayerMailId].ClaimedAt);
            Assert.Contains(
                $"Mail reward {firstEligibleMailId}",
                grantReasons);
            Assert.Contains(
                $"Mail reward {secondEligibleMailId}",
                grantReasons);
            Assert.Equal(2, grantReasons.Length);
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task ClaimAllAsync_ConcurrentRequestsDoNotGrantTwice()
    {
        var options = CreateDbContextOptions();
        var claimedAt = new DateTime(
            2026,
            8,
            28,
            2,
            0,
            0,
            DateTimeKind.Utc);
        long playerId;
        long[] mailIds;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var player = Player.Create(
                $"mail-claim-all-concurrent-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.Add(player);
            await arrangeDb.SaveChangesAsync();

            var mails = new[]
            {
                Mail.Create(
                    player.Id,
                    "Concurrent Mail 1",
                    "Reward must be granted once.",
                    claimedAt.AddHours(-1),
                    claimedAt.AddDays(1),
                    [RewardItem.Create(RewardType.Gold, 40)]),
                Mail.Create(
                    player.Id,
                    "Concurrent Mail 2",
                    "Reward must be granted once.",
                    claimedAt.AddHours(-1),
                    claimedAt.AddDays(1),
                    [RewardItem.Create(RewardType.Gem, 4)])
            };
            arrangeDb.Mails.AddRange(mails);
            await arrangeDb.SaveChangesAsync();

            playerId = player.Id;
            mailIds = mails.Select(mail => mail.Id).ToArray();
        }

        var results = await Task.WhenAll(
            ClaimWithNewDbContextAsync(
                options,
                playerId,
                claimedAt),
            ClaimWithNewDbContextAsync(
                options,
                playerId,
                claimedAt.AddMilliseconds(1)));

        Assert.Single(
            results,
            result => result.Status == MailClaimAllStatus.Claimed);
        Assert.Single(
            results,
            result => result.Status is
                MailClaimAllStatus.NothingToClaim or
                MailClaimAllStatus.ConcurrencyConflict);

        await using var assertDb = new GameDbContext(options);
        var persistedPlayer = await assertDb.Players
            .AsNoTracking()
            .SingleAsync(player => player.Id == playerId);
        var claimedMailCount = await assertDb.Mails
            .AsNoTracking()
            .CountAsync(mail =>
                mailIds.Contains(mail.Id) &&
                mail.ClaimedAt.HasValue);
        var grantCount = await assertDb.RewardGrantRecords
            .AsNoTracking()
            .CountAsync(record =>
                record.PlayerId == playerId &&
                (record.Reason == $"Mail reward {mailIds[0]}" ||
                    record.Reason == $"Mail reward {mailIds[1]}"));

        Assert.Equal(Player.InitialGold + 40, persistedPlayer.Gold);
        Assert.Equal(Player.InitialGem + 4, persistedPlayer.Gem);
        Assert.Equal(2, claimedMailCount);
        Assert.Equal(2, grantCount);
    }

    private static DbContextOptions<GameDbContext> CreateDbContextOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;

        return new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }

    private static MailClaimAllService CreateService(GameDbContext db)
    {
        return new MailClaimAllService(
            db,
            new RewardGrantService(db));
    }

    private static async Task<MailClaimAllResult>
        ClaimWithNewDbContextAsync(
            DbContextOptions<GameDbContext> options,
            long playerId,
            DateTime claimedAt)
    {
        await using var db = new GameDbContext(options);
        var service = CreateService(db);

        return await service.ClaimAllAsync(
            playerId,
            claimedAt);
    }
}
