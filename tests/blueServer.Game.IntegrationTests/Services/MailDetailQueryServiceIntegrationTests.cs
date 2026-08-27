using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Mails;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class MailDetailQueryServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task GetAsync_ReturnsOwnedMailAndHidesItFromOtherPlayer()
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
            6,
            0,
            0,
            DateTimeKind.Utc);
        var sentAt = currentTime.AddHours(-1);
        var expiresAt = currentTime.AddDays(1);
        long ownerId;
        long otherPlayerId;
        long mailId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var owner = Player.Create(
                $"mail-detail-owner-{Guid.NewGuid():N}",
                "integration-test");
            var otherPlayer = Player.Create(
                $"mail-detail-other-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.AddRange(owner, otherPlayer);
            await arrangeDb.SaveChangesAsync();

            var mail = Mail.Create(
                owner.Id,
                "Detailed reward",
                "Mail body for the owner only.",
                sentAt,
                expiresAt,
                [
                    RewardItem.Create(RewardType.Gold, 100),
                    RewardItem.Create(RewardType.Gem, 20)
                ]);
            arrangeDb.Mails.Add(mail);
            await arrangeDb.SaveChangesAsync();

            ownerId = owner.Id;
            otherPlayerId = otherPlayer.Id;
            mailId = mail.Id;
        }

        await using (var ownerDb = new GameDbContext(options))
        {
            var service = new MailDetailQueryService(ownerDb);
            var result = await service.GetAsync(
                ownerId,
                mailId,
                currentTime);

            Assert.True(result.IsSuccess);
            var mail = Assert.IsType<MailDetail>(result.Mail);
            Assert.Equal(mailId, mail.Id);
            Assert.Equal("Detailed reward", mail.Title);
            Assert.Equal("Mail body for the owner only.", mail.Body);
            Assert.Equal(sentAt, mail.SentAt);
            Assert.Equal(expiresAt, mail.ExpiresAt);
            Assert.False(mail.IsRead);
            Assert.False(mail.IsClaimed);
            Assert.False(mail.IsExpired);
            Assert.True(mail.CanClaim);
            Assert.Collection(
                mail.Attachments,
                attachment =>
                {
                    Assert.Equal(RewardType.Gold, attachment.Type);
                    Assert.Equal(100, attachment.Amount);
                },
                attachment =>
                {
                    Assert.Equal(RewardType.Gem, attachment.Type);
                    Assert.Equal(20, attachment.Amount);
                });
        }

        await using (var otherPlayerDb = new GameDbContext(options))
        {
            var service = new MailDetailQueryService(otherPlayerDb);
            var result = await service.GetAsync(
                otherPlayerId,
                mailId,
                currentTime);

            Assert.Equal(MailDetailStatus.NotFound, result.Status);
            Assert.Null(result.Mail);
        }
    }
}
