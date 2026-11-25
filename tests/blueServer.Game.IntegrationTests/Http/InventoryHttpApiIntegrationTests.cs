using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Http;

public sealed class InventoryHttpApiIntegrationTests
{
    [ApiPostgreSqlIntegrationFact]
    public async Task Inventory_ReturnsAuthenticatedWholeInventoryAndCurrentQuantities()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        using var client = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable(
                ApiPostgreSqlIntegrationFactAttribute.ApiBaseAddressEnvironmentVariable)!)
        };
        const string path = "/players/me/inventory";
        using (var unauthorized = await client.GetAsync(path, ct))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        }

        var nickname = $"inventory-http-{Guid.NewGuid():N}";
        var credentials = new { nickname, password = "integration-test-password" };
        using var register = await client.PostAsJsonAsync("/register", credentials, ct);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var login = await client.PostAsJsonAsync("/login", credentials, ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync(ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", loginJson.RootElement.GetProperty("accessToken").GetString());

        using (var empty = await client.GetAsync(path, ct))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            using var json = JsonDocument.Parse(await empty.Content.ReadAsStringAsync(ct));
            Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
        }

        var options = new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable(
                PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!).Options;
        long playerId;
        var templateIds = new List<int>();
        await using (var db = new GameDbContext(options))
        {
            playerId = await db.Players.Where(player => player.Nickname == nickname)
                .Select(player => player.Id).SingleAsync(ct);
            var other = Player.Create($"inventory-http-other-{Guid.NewGuid():N}");
            db.Players.Add(other);
            var start = Random.Shared.Next(100_000_000, 1_000_000_000);
            while (await db.ItemTemplates.AnyAsync(
                item => item.Id >= start && item.Id < start + 62, ct))
            {
                start += 62;
            }

            var templates = Enumerable.Range(start, 62).Select(id =>
            {
                var code = $"http_{Guid.NewGuid():N}";
                return ItemTemplate.Create(
                    id, code, $"item.{code}.name", $"item.{code}.description", ItemType.Material);
            }).ToArray();
            templates[59].Deactivate();
            db.ItemTemplates.AddRange(templates);
            await db.SaveChangesAsync(ct);

            // 이전 페이지 상한인 50종을 넘어도 한 번의 응답으로 전체를 반환한다.
            foreach (var template in templates.Take(60))
            {
                templateIds.Add(template.Id);
                db.PlayerItems.Add(PlayerItem.Create(playerId, template, 5));
            }
            var zero = PlayerItem.Create(playerId, templates[60], 1);
            Assert.True(zero.TryDecrease(1));
            db.PlayerItems.Add(zero);
            db.PlayerItems.Add(PlayerItem.Create(other.Id, templates[61], 999));
            await db.SaveChangesAsync(ct);
        }

        // 임의의 Player ID를 붙여도 JWT 소유자의 인벤토리만 반환한다.
        using (var response = await client.GetAsync(path + "?playerId=9223372036854775807", ct))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(templateIds, items.Select(item => item.GetProperty("itemTemplateId").GetInt32()));
            Assert.All(items, item =>
            {
                Assert.Equal(5, item.GetProperty("quantity").GetInt32());
                Assert.Equal("Material", item.GetProperty("type").GetString());
            });
            Assert.All(items.Take(59), item => Assert.True(item.GetProperty("isActive").GetBoolean()));
            Assert.False(items[59].GetProperty("isActive").GetBoolean());
        }

        await using (var db = new GameDbContext(options))
        {
            var item = await db.PlayerItems.SingleAsync(
                item => item.PlayerId == playerId && item.ItemTemplateId == templateIds[0], ct);
            Assert.True(item.TryDecrease(3));
            await db.SaveChangesAsync(ct);
        }
        using var current = await client.GetAsync(path, ct);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        using var currentJson = JsonDocument.Parse(await current.Content.ReadAsStringAsync(ct));
        Assert.Equal(2, currentJson.RootElement.GetProperty("items")[0].GetProperty("quantity").GetInt32());
    }
}
