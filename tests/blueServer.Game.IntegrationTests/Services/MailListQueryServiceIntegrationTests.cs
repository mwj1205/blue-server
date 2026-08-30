using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Mails;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class MailListQueryServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task GetAsync_ReturnsLatestMailPageAndStatus()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var currentTime = new DateTime(
            2026,
            8,
            27,
            3,
            0,
            0,
            DateTimeKind.Utc);
        long playerId;
        long newestMailId;
        long expiredMailId;
        long readMailId;
        long claimedMailId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var player = Player.Create(
                $"mail-list-integration-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.Add(player);
            await arrangeDb.SaveChangesAsync();

            var claimedMail = Mail.Create(
                player.Id,
                "Claimed reward",
                "Already claimed reward mail.",
                currentTime.AddDays(-4),
                currentTime.AddDays(1),
                [RewardItem.Create(RewardType.Gold, 10)]);
            var readMail = Mail.Create(
                player.Id,
                "Read notice",
                "Notice without rewards.",
                currentTime.AddDays(-3));
            var expiredMail = Mail.Create(
                player.Id,
                "Expired reward",
                "Expired reward mail.",
                currentTime.AddDays(-2),
                currentTime.AddDays(-1),
                [RewardItem.Create(RewardType.Gem, 5)]);
            var newestMail = Mail.Create(
                player.Id,
                "New reward",
                "Latest reward mail.",
                currentTime.AddHours(-1),
                currentTime.AddDays(1),
                [RewardItem.Create(RewardType.Gold, 100)]);

            readMail.MarkAsRead(currentTime.AddDays(-2));
            claimedMail.Claim(currentTime.AddDays(-3));

            arrangeDb.Mails.AddRange(
                claimedMail,
                readMail,
                expiredMail,
                newestMail);
            await arrangeDb.SaveChangesAsync();

            playerId = player.Id;
            newestMailId = newestMail.Id;
            expiredMailId = expiredMail.Id;
            readMailId = readMail.Id;
            claimedMailId = claimedMail.Id;
        }

        MailListResult firstPage;

        await using (var firstPageDb = new GameDbContext(options))
        {
            var service = new MailListQueryService(firstPageDb);
            firstPage = await service.GetAsync(
                playerId,
                currentTime,
                pageSize: 2);
        }

        Assert.True(firstPage.IsSuccess);
        Assert.NotNull(firstPage.NextCursor);
        Assert.Collection(
            firstPage.Items,
            mail =>
            {
                Assert.Equal(newestMailId, mail.Id);
                Assert.False(mail.IsRead);
                Assert.False(mail.IsClaimed);
                Assert.False(mail.IsExpired);
                Assert.Equal(1, mail.AttachmentCount);
                Assert.True(mail.CanClaim);
            },
            mail =>
            {
                Assert.Equal(expiredMailId, mail.Id);
                Assert.True(mail.IsExpired);
                Assert.False(mail.CanClaim);
            });

        await using (var secondPageDb = new GameDbContext(options))
        {
            var service = new MailListQueryService(secondPageDb);
            var secondPage = await service.GetAsync(
                playerId,
                currentTime,
                pageSize: 2,
                cursor: firstPage.NextCursor);

            Assert.True(secondPage.IsSuccess);
            Assert.Null(secondPage.NextCursor);
            Assert.Collection(
                secondPage.Items,
                mail =>
                {
                    Assert.Equal(readMailId, mail.Id);
                    Assert.True(mail.IsRead);
                    Assert.Equal(0, mail.AttachmentCount);
                    Assert.False(mail.CanClaim);
                },
                mail =>
                {
                    Assert.Equal(claimedMailId, mail.Id);
                    Assert.True(mail.IsRead);
                    Assert.True(mail.IsClaimed);
                    Assert.False(mail.CanClaim);
                });
        }
    }
}
