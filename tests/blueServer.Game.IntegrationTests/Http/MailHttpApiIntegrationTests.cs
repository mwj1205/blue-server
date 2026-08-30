using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Http;

public sealed class MailHttpApiIntegrationTests
{
    [ApiPostgreSqlIntegrationFact]
    public async Task MailEndpoints_CompleteAuthenticatedClaimFlow()
    {
        var options = CreateDbContextOptions();
        var nickname = $"mail-http-{Guid.NewGuid():N}";
        const string password = "integration-test-password";
        var apiBaseAddress = Environment.GetEnvironmentVariable(
            ApiPostgreSqlIntegrationFactAttribute
                .ApiBaseAddressEnvironmentVariable)!;

        using var client = new HttpClient
        {
            BaseAddress = new Uri(apiBaseAddress, UriKind.Absolute)
        };

        using (var unauthorizedResponse = await client.GetAsync(
            "/players/me/mails"))
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                unauthorizedResponse.StatusCode);
        }

        // 실제 인증 Pipeline을 통과하는 Player와 JWT 준비
        using var registerResponse = await client.PostAsJsonAsync(
            "/register",
            new
            {
                nickname,
                password
            });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        using var loginResponse = await client.PostAsJsonAsync(
            "/login",
            new
            {
                nickname,
                password
            });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var loginBody = await ReadJsonAsync(loginResponse);
        var accessToken = loginBody.RootElement
            .GetProperty("accessToken")
            .GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        long playerId;
        long singleClaimMailId;
        long claimAllMailId;
        long expiredMailId;
        long otherPlayerMailId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var player = await arrangeDb.Players
                .SingleAsync(player => player.Nickname == nickname);
            var otherPlayer = Player.Create(
                $"mail-http-other-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.Add(otherPlayer);
            await arrangeDb.SaveChangesAsync();

            var sentAt = DateTime.UtcNow.AddMinutes(-5);
            var singleClaimMail = Mail.Create(
                player.Id,
                "HTTP single claim Mail",
                "This Mail verifies read and single claim endpoints.",
                sentAt,
                sentAt.AddDays(1),
                [
                    RewardItem.Create(RewardType.Gold, 120),
                    RewardItem.Create(RewardType.Gem, 15)
                ]);
            var claimAllMail = Mail.Create(
                player.Id,
                "HTTP claim-all Mail",
                "This Mail verifies the claim-all endpoint.",
                sentAt.AddMinutes(1),
                sentAt.AddDays(1),
                [
                    RewardItem.Create(RewardType.Gold, 30),
                    RewardItem.Create(RewardType.Gem, 5)
                ]);
            var expiredMail = Mail.Create(
                player.Id,
                "Expired HTTP Mail",
                "This Mail verifies the expired claim response.",
                sentAt,
                sentAt.AddMinutes(1),
                [RewardItem.Create(RewardType.Gold, 999)]);
            var otherPlayerMail = Mail.Create(
                otherPlayer.Id,
                "Other Player Mail",
                "This Mail must not be exposed.",
                sentAt,
                sentAt.AddDays(1),
                [RewardItem.Create(RewardType.Gold, 999)]);

            arrangeDb.Mails.AddRange(
                singleClaimMail,
                claimAllMail,
                expiredMail,
                otherPlayerMail);
            await arrangeDb.SaveChangesAsync();

            playerId = player.Id;
            singleClaimMailId = singleClaimMail.Id;
            claimAllMailId = claimAllMail.Id;
            expiredMailId = expiredMail.Id;
            otherPlayerMailId = otherPlayerMail.Id;
        }

        using (var invalidCursorResponse = await client.GetAsync(
            "/players/me/mails?cursorId=1"))
        {
            Assert.Equal(
                HttpStatusCode.BadRequest,
                invalidCursorResponse.StatusCode);
        }

        using (var listResponse = await client.GetAsync(
            "/players/me/mails?pageSize=10"))
        {
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            using var body = await ReadJsonAsync(listResponse);
            var mailIds = body.RootElement
                .GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("id").GetInt64())
                .ToArray();

            Assert.Contains(singleClaimMailId, mailIds);
            Assert.Contains(claimAllMailId, mailIds);
            Assert.DoesNotContain(otherPlayerMailId, mailIds);
        }

        using (var detailResponse = await client.GetAsync(
            $"/players/me/mails/{singleClaimMailId}"))
        {
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            using var body = await ReadJsonAsync(detailResponse);
            Assert.Equal(
                singleClaimMailId,
                body.RootElement.GetProperty("id").GetInt64());
            Assert.Equal(
                2,
                body.RootElement
                    .GetProperty("attachments")
                    .GetArrayLength());
        }

        using (var otherPlayerResponse = await client.GetAsync(
            $"/players/me/mails/{otherPlayerMailId}"))
        {
            Assert.Equal(
                HttpStatusCode.NotFound,
                otherPlayerResponse.StatusCode);
        }

        using (var expiredClaimResponse = await client.PostAsync(
            $"/players/me/mails/{expiredMailId}/claim",
            null))
        {
            Assert.Equal(
                HttpStatusCode.Conflict,
                expiredClaimResponse.StatusCode);
        }

        DateTime firstReadAt;

        using (var readResponse = await SendWithoutBodyAsync(
            client,
            HttpMethod.Put,
            $"/players/me/mails/{singleClaimMailId}/read"))
        {
            Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
            using var body = await ReadJsonAsync(readResponse);
            firstReadAt = body.RootElement
                .GetProperty("readAt")
                .GetDateTime();
            Assert.False(
                body.RootElement
                    .GetProperty("wasAlreadyRead")
                    .GetBoolean());
        }

        using (var retryReadResponse = await SendWithoutBodyAsync(
            client,
            HttpMethod.Put,
            $"/players/me/mails/{singleClaimMailId}/read"))
        {
            Assert.Equal(HttpStatusCode.OK, retryReadResponse.StatusCode);
            using var body = await ReadJsonAsync(retryReadResponse);
            Assert.True(
                body.RootElement
                    .GetProperty("wasAlreadyRead")
                    .GetBoolean());
            Assert.Equal(
                firstReadAt,
                body.RootElement.GetProperty("readAt").GetDateTime(),
                TimeSpan.FromMicroseconds(1));
        }

        using (var claimResponse = await client.PostAsync(
            $"/players/me/mails/{singleClaimMailId}/claim",
            null))
        {
            Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
            using var body = await ReadJsonAsync(claimResponse);
            Assert.False(
                body.RootElement
                    .GetProperty("wasAlreadyClaimed")
                    .GetBoolean());
            Assert.Equal(
                Player.InitialGold + 120,
                body.RootElement.GetProperty("currentGold").GetInt32());
            Assert.Equal(
                Player.InitialGem + 15,
                body.RootElement.GetProperty("currentGem").GetInt32());
        }

        using (var retryClaimResponse = await client.PostAsync(
            $"/players/me/mails/{singleClaimMailId}/claim",
            null))
        {
            Assert.Equal(HttpStatusCode.OK, retryClaimResponse.StatusCode);
            using var body = await ReadJsonAsync(retryClaimResponse);
            Assert.True(
                body.RootElement
                    .GetProperty("wasAlreadyClaimed")
                    .GetBoolean());
            Assert.Equal(
                Player.InitialGold + 120,
                body.RootElement.GetProperty("currentGold").GetInt32());
        }

        using (var claimAllResponse = await client.PostAsync(
            "/players/me/mails/claim-all",
            null))
        {
            Assert.Equal(HttpStatusCode.OK, claimAllResponse.StatusCode);
            using var body = await ReadJsonAsync(claimAllResponse);
            Assert.Equal(
                1,
                body.RootElement
                    .GetProperty("claimedMailCount")
                    .GetInt32());
            Assert.Equal(
                30,
                body.RootElement.GetProperty("grantedGold").GetInt32());
            Assert.Equal(
                5,
                body.RootElement.GetProperty("grantedGem").GetInt32());
            Assert.Equal(
                Player.InitialGold + 150,
                body.RootElement.GetProperty("currentGold").GetInt32());
            Assert.Equal(
                Player.InitialGem + 20,
                body.RootElement.GetProperty("currentGem").GetInt32());
            Assert.False(
                body.RootElement.GetProperty("hasMore").GetBoolean());
        }

        using (var retryClaimAllResponse = await client.PostAsync(
            "/players/me/mails/claim-all",
            null))
        {
            Assert.Equal(HttpStatusCode.OK, retryClaimAllResponse.StatusCode);
            using var body = await ReadJsonAsync(retryClaimAllResponse);
            Assert.Equal(
                0,
                body.RootElement
                    .GetProperty("claimedMailCount")
                    .GetInt32());
            Assert.Equal(
                Player.InitialGold + 150,
                body.RootElement.GetProperty("currentGold").GetInt32());
            Assert.Equal(
                Player.InitialGem + 20,
                body.RootElement.GetProperty("currentGem").GetInt32());
        }

        await using var assertDb = new GameDbContext(options);
        var persistedPlayer = await assertDb.Players
            .AsNoTracking()
            .SingleAsync(player => player.Id == playerId);
        var mails = await assertDb.Mails
            .AsNoTracking()
            .Where(mail =>
                mail.Id == singleClaimMailId ||
                mail.Id == claimAllMailId ||
                mail.Id == expiredMailId ||
                mail.Id == otherPlayerMailId)
            .ToDictionaryAsync(mail => mail.Id);
        var grantCount = await assertDb.RewardGrantRecords
            .AsNoTracking()
            .CountAsync(record =>
                record.PlayerId == playerId &&
                (record.Reason == $"Mail reward {singleClaimMailId}" ||
                    record.Reason == $"Mail reward {claimAllMailId}"));

        Assert.Equal(Player.InitialGold + 150, persistedPlayer.Gold);
        Assert.Equal(Player.InitialGem + 20, persistedPlayer.Gem);
        Assert.NotNull(mails[singleClaimMailId].ReadAt);
        Assert.NotNull(mails[singleClaimMailId].ClaimedAt);
        Assert.NotNull(mails[claimAllMailId].ReadAt);
        Assert.NotNull(mails[claimAllMailId].ClaimedAt);
        Assert.Null(mails[expiredMailId].ReadAt);
        Assert.Null(mails[expiredMailId].ClaimedAt);
        Assert.Null(mails[otherPlayerMailId].ReadAt);
        Assert.Null(mails[otherPlayerMailId].ClaimedAt);
        Assert.Equal(2, grantCount);
    }

    private static DbContextOptions<GameDbContext> CreateDbContextOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute
                .ConnectionStringEnvironmentVariable)!;

        return new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }

    private static async Task<HttpResponseMessage> SendWithoutBodyAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        await using var responseStream = await response.Content
            .ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(responseStream);
    }
}
