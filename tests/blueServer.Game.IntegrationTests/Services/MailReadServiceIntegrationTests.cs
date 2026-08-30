using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Mails;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class MailReadServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task MarkAsReadAsync_PreservesFirstReadTimeAndChecksOwnership()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var sentAt = new DateTime(
            2026,
            8,
            27,
            7,
            0,
            0,
            DateTimeKind.Utc);
        var firstReadAt = sentAt.AddHours(1);
        var retryReadAt = sentAt.AddHours(2);
        long ownerId;
        long otherPlayerId;
        long mailId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var owner = Player.Create(
                $"mail-read-owner-{Guid.NewGuid():N}",
                "integration-test");
            var otherPlayer = Player.Create(
                $"mail-read-other-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.AddRange(owner, otherPlayer);
            await arrangeDb.SaveChangesAsync();

            var mail = Mail.Create(
                owner.Id,
                "Read status test",
                "The first read time must be preserved.",
                sentAt);
            arrangeDb.Mails.Add(mail);
            await arrangeDb.SaveChangesAsync();

            ownerId = owner.Id;
            otherPlayerId = otherPlayer.Id;
            mailId = mail.Id;
        }

        await using (var firstReadDb = new GameDbContext(options))
        {
            var service = new MailReadService(firstReadDb);
            var result = await service.MarkAsReadAsync(
                ownerId,
                mailId,
                firstReadAt);

            Assert.True(result.IsSuccess);
            Assert.Equal(MailReadStatus.MarkedAsRead, result.Status);
            Assert.Equal(firstReadAt, result.ReadAt);
        }

        await using (var retryDb = new GameDbContext(options))
        {
            var service = new MailReadService(retryDb);
            var result = await service.MarkAsReadAsync(
                ownerId,
                mailId,
                retryReadAt);

            Assert.True(result.IsSuccess);
            Assert.Equal(MailReadStatus.AlreadyRead, result.Status);
            Assert.Equal(firstReadAt, result.ReadAt);
        }

        await using (var otherPlayerDb = new GameDbContext(options))
        {
            var service = new MailReadService(otherPlayerDb);
            var result = await service.MarkAsReadAsync(
                otherPlayerId,
                mailId,
                retryReadAt);

            Assert.Equal(MailReadStatus.NotFound, result.Status);
            Assert.Null(result.ReadAt);
        }

        await using (var assertDb = new GameDbContext(options))
        {
            var persistedReadAt = await assertDb.Mails
                .Where(mail => mail.Id == mailId)
                .Select(mail => mail.ReadAt)
                .SingleAsync();

            Assert.Equal(firstReadAt, persistedReadAt);
        }
    }
}
