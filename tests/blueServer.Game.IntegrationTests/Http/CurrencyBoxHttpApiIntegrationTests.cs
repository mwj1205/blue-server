using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Http;

public sealed class CurrencyBoxHttpApiIntegrationTests
{
    [ApiPostgreSqlIntegrationFact]
    public async Task CurrencyBoxes_ValidateAuthenticateUseAndReplay()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        using var client = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable(
                ApiPostgreSqlIntegrationFactAttribute.ApiBaseAddressEnvironmentVariable)!)
        };
        const string path = "/players/me/currency-boxes/3101/use";
        using (var unauthorized = await client.PostAsJsonAsync(
            path, new { quantity = 1, requestId = Guid.NewGuid() }, ct))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        }

        var nickname = $"box-http-{Guid.NewGuid():N}";
        var credentials = new { nickname, password = "integration-test-password" };
        using var register = await client.PostAsJsonAsync("/register", credentials, ct);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var login = await client.PostAsJsonAsync("/login", credentials, ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync(ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", loginJson.RootElement.GetProperty("accessToken").GetString());

        var options = new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable(PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!)
            .Options;
        long playerId;
        int[] boxIds = [3101, 3102, 3103, 3104];
        await using (var db = new GameDbContext(options))
        {
            playerId = await db.Players.Where(player => player.Nickname == nickname)
                .Select(player => player.Id).SingleAsync(ct);
            var templates = await db.ItemTemplates.Where(template => boxIds.Contains(template.Id)).ToArrayAsync(ct);
            Assert.Equal(4, templates.Length);
            db.PlayerItems.AddRange(templates.Select(template => PlayerItem.Create(playerId, template, 2)));
            await db.SaveChangesAsync(ct);
        }

        foreach (var quantity in new[] { 0, -1, 1_000_000 })
        {
            using var invalid = await client.PostAsJsonAsync(path, new { quantity, requestId = Guid.NewGuid() }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using (var emptyId = await client.PostAsJsonAsync(path, new { quantity = 1, requestId = Guid.Empty }, ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, emptyId.StatusCode);
        }

        // null 본문이나 필수 필드 누락도 서비스 호출 전 거절해야 한다.
        foreach (var body in new[] { "null", "{}", "{\"quantity\":1}", "{\"requestId\":\"bad-guid\"}" })
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var invalid = await client.PostAsync(path, content, ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using (var invalidId = await client.PostAsJsonAsync(
            "/players/me/currency-boxes/0/use", new { quantity = 1, requestId = Guid.NewGuid() }, ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalidId.StatusCode);
        }

        using (var notABox = await client.PostAsJsonAsync(
            "/players/me/currency-boxes/1001/use", new { quantity = 1, requestId = Guid.NewGuid() }, ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, notABox.StatusCode);
        }

        using (var insufficient = await client.PostAsJsonAsync(path, new { quantity = 3, requestId = Guid.NewGuid() }, ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, insufficient.StatusCode);
            using var json = JsonDocument.Parse(await insufficient.Content.ReadAsStringAsync(ct));
            Assert.Equal("InsufficientQuantity", json.RootElement.GetProperty("code").GetString());
        }

        await using (var db = new GameDbContext(options))
        {
            Assert.Equal(8, await db.PlayerItems.Where(item => item.PlayerId == playerId).SumAsync(item => item.Quantity, ct));
            Assert.False(await db.CurrencyBoxUseRecords.AnyAsync(record => record.PlayerId == playerId, ct));
        }

        var gold = Player.InitialGold;
        var gem = Player.InitialGem;
        foreach (var boxId in boxIds)
        {
            var requestId = Guid.NewGuid();
            var url = $"/players/me/currency-boxes/{boxId}/use";
            var body = new { quantity = 2, requestId };
            using var used = await client.PostAsJsonAsync(url, body, ct);
            Assert.Equal(HttpStatusCode.OK, used.StatusCode);
            var response = await used.Content.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(response);
            var root = json.RootElement;
            Assert.Equal(requestId, root.GetProperty("requestId").GetGuid());
            Assert.Equal(boxId, root.GetProperty("itemTemplateId").GetInt32());
            Assert.Equal(2, root.GetProperty("usedQuantity").GetInt32());
            Assert.Equal(0, root.GetProperty("quantityAfter").GetInt32());
            var amount = root.GetProperty("grantedAmount").GetInt32();
            if (boxId < 3103)
            {
                Assert.Equal(200, amount);
            }
            else
            {
                Assert.InRange(amount, 40, 400);
            }

            var isGold = boxId is 3101 or 3103;
            Assert.Equal(isGold ? "Gold" : "Gem", root.GetProperty("currencyType").GetString());
            gold += isGold ? amount : 0;
            gem += isGold ? 0 : amount;
            Assert.Equal(gold, root.GetProperty("currentGold").GetInt32());
            Assert.Equal(gem, root.GetProperty("currentGem").GetInt32());
            Assert.Equal(DateTimeKind.Utc, root.GetProperty("processedAt").GetDateTime().Kind);

            using var replay = await client.PostAsJsonAsync(url, body, ct);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(response, await replay.Content.ReadAsStringAsync(ct));
            using var conflict = await client.PostAsJsonAsync(url, new { quantity = 1, requestId }, ct);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            using var conflictJson = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync(ct));
            Assert.Equal("IdempotencyConflict", conflictJson.RootElement.GetProperty("code").GetString());
        }

        await using var verify = new GameDbContext(options);
        Assert.Equal(0, await verify.PlayerItems.Where(item => item.PlayerId == playerId).SumAsync(item => item.Quantity, ct));
        var player = await verify.Players.SingleAsync(player => player.Id == playerId, ct);
        Assert.Equal(gold, player.Gold);
        Assert.Equal(gem, player.Gem);
        Assert.Equal(4, await verify.CurrencyBoxUseRecords.CountAsync(record => record.PlayerId == playerId, ct));
        Assert.Equal(4, await verify.InventoryItemChangeLogs.CountAsync(log => log.PlayerId == playerId, ct));
        Assert.Equal(4, await verify.CurrencyChangeLogs.CountAsync(log => log.PlayerId == playerId, ct));
        Assert.Equal(4, await verify.RewardGrantRecords.CountAsync(record => record.PlayerId == playerId, ct));
    }
}
