using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Currencies;
using blueServer.Infrastructure.Items;
using blueServer.Infrastructure.Mails;
using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Http;

public sealed partial class MailHttpApiIntegrationTests
{
    [ApiPostgreSqlIntegrationFact]
    public async Task MailEndpoints_PartialItemClaimUsesRequestIdAndKeepsRemainder()
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable(
                ApiPostgreSqlIntegrationFactAttribute.ApiBaseAddressEnvironmentVariable)!)
        };
        var nickname = $"mail-http-items-{Guid.NewGuid():N}";
        var credentials = new { nickname, password = "integration-test-password" };
        using var register = await client.PostAsJsonAsync("/register", credentials);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var login = await client.PostAsJsonAsync("/login", credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginBody = await ReadJsonAsync(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", loginBody.RootElement.GetProperty("accessToken").GetString());

        var options = CreateDbContextOptions();
        long playerId;
        long mailId;
        int templateId;
        DateTime expiresAt;
        await using (var db = new GameDbContext(options))
        {
            var player = await db.Players.SingleAsync(player => player.Nickname == nickname);
            playerId = player.Id;
            templateId = await FindUnusedHttpTemplateIdAsync(db);
            var code = $"http_item_{Guid.NewGuid():N}";
            var template = ItemTemplate.Create(templateId, code, $"item.{code}.name",
                $"item.{code}.description", ItemType.Material);
            db.ItemTemplates.Add(template);
            db.PlayerItems.Add(PlayerItem.Create(playerId, template, 999000));
            var sentAt = DateTime.UtcNow.AddMinutes(-5);
            sentAt = new DateTime(sentAt.Ticks - sentAt.Ticks % 10, DateTimeKind.Utc);
            expiresAt = sentAt.AddDays(1);
            var mail = Mail.Create(playerId, "HTTP item mail", "Preserve partial Mail",
                sentAt, expiresAt, [CurrencyReward.Create(RewardType.Gold, 100)],
                inventoryItemRewards: [InventoryItemReward.Create(templateId, 1000)]);
            var currencyMail = Mail.Create(playerId, "Currency-only Mail", "Claim-all supported",
                sentAt, expiresAt, [CurrencyReward.Create(RewardType.Gold, 30)]);
            db.Mails.AddRange(mail, currencyMail);
            await db.SaveChangesAsync();
            mailId = mail.Id;
        }

        // RequestId 미지원 기존 TCP 서비스 경로는 아이템 Mail을 변경하지 않고 거부한다.
        await using (var db = new GameDbContext(options))
        {
            var legacy = await new MailClaimService(db,
                new RewardGrantService(db, new CurrencyChangeService(db)), new InventoryChangeService(db))
                .ClaimAsync(playerId, mailId, DateTime.UtcNow);
            Assert.Equal(MailClaimStatus.RequestIdRequired, legacy.Status);
        }

        var uri = $"/players/me/mails/{mailId}/claim";
        using (var missing = await client.PostAsync(uri, null))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        }
        using (var empty = await client.PostAsJsonAsync(uri, new { requestId = Guid.Empty }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        }
        using (var content = new StringContent("{", System.Text.Encoding.UTF8, "application/json"))
        using (var malformed = await client.PostAsync(uri, content))
        {
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        }

        var requestId = Guid.NewGuid();
        using var firstResponse = await client.PostAsJsonAsync(uri, new { requestId });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var first = await ReadJsonAsync(firstResponse);
        Assert.Equal("PartiallyClaimed", first.RootElement.GetProperty("status").GetString());
        Assert.Equal(requestId, first.RootElement.GetProperty("requestId").GetGuid());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.RootElement.GetProperty("claimedAt").ValueKind);
        Assert.Equal(100, first.RootElement.GetProperty("grantedGold").GetInt32());
        Assert.Equal(999, first.RootElement.GetProperty("items")[0].GetProperty("grantedQuantity").GetInt32());
        Assert.Equal(1, first.RootElement.GetProperty("items")[0].GetProperty("remainingQuantity").GetInt32());

        using (var batch = await client.PostAsync("/players/me/mails/claim-all", null))
        {
            Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
            using var body = await ReadJsonAsync(batch);
            Assert.Equal(1, body.RootElement.GetProperty("claimedMailCount").GetInt32());
            Assert.Equal(30, body.RootElement.GetProperty("grantedGold").GetInt32());
        }

        using (var retryResponse = await client.PostAsJsonAsync(uri, new { requestId }))
        {
            Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
            using var retry = await ReadJsonAsync(retryResponse);
            Assert.Equal(first.RootElement.GetProperty("processedAt").GetDateTime(),
                retry.RootElement.GetProperty("processedAt").GetDateTime());
            Assert.Equal(Player.InitialGold + 100, retry.RootElement.GetProperty("currentGold").GetInt32());
            Assert.Equal("PartiallyClaimed", retry.RootElement.GetProperty("status").GetString());
            Assert.Equal(999, retry.RootElement.GetProperty("items")[0].GetProperty("grantedQuantity").GetInt32());
        }
        using (var conflict = await client.PostAsJsonAsync(
            $"/players/me/mails/{mailId + 1}/claim", new { requestId }))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            using var body = await ReadJsonAsync(conflict);
            Assert.Equal("IdempotencyConflict", body.RootElement.GetProperty("code").GetString());
        }
        using (var detailResponse = await client.GetAsync($"/players/me/mails/{mailId}"))
        {
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            using var detail = await ReadJsonAsync(detailResponse);
            Assert.Equal(expiresAt, detail.RootElement.GetProperty("expiresAt").GetDateTime());
            Assert.False(detail.RootElement.GetProperty("isClaimed").GetBoolean());
            Assert.Equal(999, detail.RootElement.GetProperty("itemAttachments")[0].GetProperty("claimedQuantity").GetInt32());
            Assert.Equal(1, detail.RootElement.GetProperty("itemAttachments")[0].GetProperty("remainingQuantity").GetInt32());
        }
        using (var fullResponse = await client.PostAsJsonAsync(uri, new { requestId = Guid.NewGuid() }))
        {
            Assert.Equal(HttpStatusCode.OK, fullResponse.StatusCode);
            using var full = await ReadJsonAsync(fullResponse);
            Assert.Equal("NoCapacity", full.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, full.RootElement.GetProperty("items")[0].GetProperty("grantedQuantity").GetInt32());
        }
        await using (var db = new GameDbContext(options))
        {
            var item = await db.PlayerItems.SingleAsync(item => item.PlayerId == playerId);
            Assert.True(item.TryDecrease(1));
            await db.SaveChangesAsync();
        }
        using var completeResponse = await client.PostAsJsonAsync(uri, new { requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        using var completed = await ReadJsonAsync(completeResponse);
        Assert.Equal("Claimed", completed.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, completed.RootElement.GetProperty("grantedGold").GetInt32());
        Assert.Equal(Player.InitialGold + 130, completed.RootElement.GetProperty("currentGold").GetInt32());
        Assert.Equal(1, completed.RootElement.GetProperty("items")[0].GetProperty("grantedQuantity").GetInt32());
        Assert.Equal(0, completed.RootElement.GetProperty("items")[0].GetProperty("remainingQuantity").GetInt32());

        await using var assertDb = new GameDbContext(options);
        Assert.Equal(3, await assertDb.MailClaimRecords.CountAsync(record => record.PlayerId == playerId));
        var finalMail = await assertDb.Mails.SingleAsync(mail => mail.Id == mailId);
        Assert.Equal(expiresAt, finalMail.ExpiresAt);
        Assert.NotNull(finalMail.ClaimedAt);
    }

    private static async Task<int> FindUnusedHttpTemplateIdAsync(GameDbContext db)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var id = Random.Shared.Next(1, int.MaxValue);
            if (!await db.ItemTemplates.AnyAsync(template => template.Id == id)) { return id; }
        }
        throw new InvalidOperationException("Cannot allocate test ItemTemplate id.");
    }
}
