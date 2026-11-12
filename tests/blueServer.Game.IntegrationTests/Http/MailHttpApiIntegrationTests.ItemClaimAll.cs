using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Http;

public sealed partial class MailHttpApiIntegrationTests
{
    [ApiPostgreSqlIntegrationFact]
    public async Task MailEndpoints_ClaimAllSupportsItemRequestsAndLegacyEmptyBody()
    {
        const string uri = "/players/me/mails/claim-all";
        using var client = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable(
                ApiPostgreSqlIntegrationFactAttribute.ApiBaseAddressEnvironmentVariable)!)
        };
        var requestId = Guid.NewGuid();
        using var anonymous = await client.PostAsJsonAsync(uri, new { requestId });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var nickname = $"mail-http-claim-all-{Guid.NewGuid():N}";
        var credentials = new { nickname, password = "integration-test-password" };
        using var register = await client.PostAsJsonAsync("/register", credentials);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var login = await client.PostAsJsonAsync("/login", credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginBody = await ReadJsonAsync(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            loginBody.RootElement.GetProperty("accessToken").GetString());

        var options = CreateDbContextOptions();
        long playerId;
        long mailId;
        int templateId;
        var sentAt = DateTime.UtcNow.AddMinutes(-5);
        sentAt = new DateTime(sentAt.Ticks - sentAt.Ticks % 10, DateTimeKind.Utc);
        var expiresAt = sentAt.AddDays(1);
        await using (var db = new GameDbContext(options))
        {
            var player = await db.Players.SingleAsync(player => player.Nickname == nickname);
            playerId = player.Id;
            templateId = await FindUnusedHttpTemplateIdAsync(db);
            var code = $"http_batch_{Guid.NewGuid():N}";
            var template = ItemTemplate.Create(
                templateId,
                code,
                $"item.{code}.name",
                $"item.{code}.description",
                ItemType.Material);
            db.ItemTemplates.Add(template);
            db.PlayerItems.Add(PlayerItem.Create(playerId, template, 999000));
            var itemMail = Mail.Create(
                playerId,
                "HTTP item claim all",
                "Keep the remaining quantity",
                sentAt,
                expiresAt,
                [CurrencyReward.Create(RewardType.Gold, 100)],
                inventoryItemRewards: [InventoryItemReward.Create(templateId, 1000)]);
            var currencyMail = Mail.Create(
                playerId,
                "HTTP legacy claim all",
                "Currency only",
                sentAt,
                expiresAt,
                [CurrencyReward.Create(RewardType.Gold, 30)]);
            db.Mails.AddRange(itemMail, currencyMail);
            await db.SaveChangesAsync();
            mailId = itemMail.Id;
        }

        string[] invalidBodies =
        [
            "null",
            "{}",
            "{",
            JsonSerializer.Serialize(new { requestId = Guid.Empty })
        ];
        foreach (var invalidJson in invalidBodies)
        {
            using var content = new StringContent(invalidJson, Encoding.UTF8, "application/json");
            using var invalid = await client.PostAsync(uri, content);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using var legacyResponse = await client.PostAsync(uri, null);
        Assert.Equal(HttpStatusCode.OK, legacyResponse.StatusCode);
        using var legacy = await ReadJsonAsync(legacyResponse);
        Assert.Equal(1, legacy.RootElement.GetProperty("claimedMailCount").GetInt32());
        Assert.Equal(30, legacy.RootElement.GetProperty("grantedGold").GetInt32());
        Assert.False(legacy.RootElement.TryGetProperty("entries", out _));

        using var firstResponse = await client.PostAsJsonAsync(uri, new { requestId });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var first = await ReadJsonAsync(firstResponse);
        var result = first.RootElement;
        Assert.Equal(requestId, result.GetProperty("requestId").GetGuid());
        Assert.Equal("Processed", result.GetProperty("status").GetString());
        Assert.Equal(0, result.GetProperty("claimedMailCount").GetInt32());
        Assert.Equal(100, result.GetProperty("grantedGold").GetInt32());
        Assert.Equal(Player.InitialGold + 130, result.GetProperty("currentGold").GetInt32());
        var entry = Assert.Single(result.GetProperty("entries").EnumerateArray());
        Assert.Equal(mailId, entry.GetProperty("mailId").GetInt64());
        Assert.Equal("PartiallyClaimed", entry.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("claimedAt").ValueKind);
        var item = Assert.Single(entry.GetProperty("items").EnumerateArray());
        Assert.Equal(templateId, item.GetProperty("itemTemplateId").GetInt32());
        Assert.Equal(999, item.GetProperty("grantedQuantity").GetInt32());
        Assert.Equal(1, item.GetProperty("remainingQuantity").GetInt32());
        Assert.Equal(999999, item.GetProperty("quantityAfter").GetInt32());

        using var replayResponse = await client.PostAsJsonAsync(uri, new { requestId });
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        using var replay = await ReadJsonAsync(replayResponse);
        Assert.Equal(result.GetRawText(), replay.RootElement.GetRawText());

        using var fullResponse = await client.PostAsJsonAsync(uri, new { requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, fullResponse.StatusCode);
        using var full = await ReadJsonAsync(fullResponse);
        Assert.Equal(0, full.RootElement.GetProperty("grantedGold").GetInt32());
        var fullEntry = Assert.Single(full.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Equal("NoCapacity", fullEntry.GetProperty("status").GetString());

        await using var finalDb = new GameDbContext(options);
        Assert.Equal(2, await finalDb.MailClaimAllRecords.CountAsync(record => record.PlayerId == playerId));
        var finalMail = await finalDb.Mails.Include(mail => mail.ItemAttachments)
            .SingleAsync(mail => mail.Id == mailId);
        Assert.Equal(expiresAt, finalMail.ExpiresAt);
        Assert.Null(finalMail.ClaimedAt);
        Assert.Equal(1, Assert.Single(finalMail.ItemAttachments).RemainingQuantity);
    }
}
