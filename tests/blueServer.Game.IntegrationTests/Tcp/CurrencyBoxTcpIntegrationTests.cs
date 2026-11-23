using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;
using blueServer.Game.Packets;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Tcp;

public sealed class CurrencyBoxTcpIntegrationTests
{
    [GameTcpPostgreSqlIntegrationFact]
    public async Task CurrencyBoxes_UseReplayAndRejectConflictsThroughAuthenticatedTcp()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        using var http = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable(
                ApiPostgreSqlIntegrationFactAttribute.ApiBaseAddressEnvironmentVariable)!)
        };
        var nickname = $"box-tcp-{Guid.NewGuid():N}";
        var credentials = new { nickname, password = "integration-test-password" };
        using var register = await http.PostAsJsonAsync("/register", credentials, ct);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var login = await http.PostAsJsonAsync("/login", credentials, ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync(ct));
        var token = loginJson.RootElement.GetProperty("accessToken").GetString()!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var options = new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable(PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!)
            .Options;
        int[] boxIds = [3101, 3102, 3103, 3104];
        long playerId;
        await using (var db = new GameDbContext(options))
        {
            playerId = await db.Players.Where(player => player.Nickname == nickname)
                .Select(player => player.Id).SingleAsync(ct);
            var templates = await db.ItemTemplates.Where(template => boxIds.Contains(template.Id)).ToArrayAsync(ct);
            Assert.Equal(4, templates.Length);
            db.PlayerItems.AddRange(templates.Select(template => PlayerItem.Create(playerId, template, 10)));
            await db.SaveChangesAsync(ct);
        }

        using var client = new TcpClient();
        await client.ConnectAsync(
            Environment.GetEnvironmentVariable(GameTcpPostgreSqlIntegrationFactAttribute.GameHostEnvironmentVariable) ?? "127.0.0.1",
            int.Parse(Environment.GetEnvironmentVariable(GameTcpPostgreSqlIntegrationFactAttribute.GamePortEnvironmentVariable)!),
            ct);
        await using var stream = client.GetStream();

        // 미인증 요청 뒤 Ping 응답을 읽어, 사용 응답이 끼어들지 않고 세션도 유지되는지 확인한다.
        await stream.WriteAsync(Request(3101, 2, Guid.NewGuid()), ct);
        var ping = new PacketWriter();
        ping.WriteUShort(4);
        ping.WriteUShort((ushort)Opcode.Ping);
        await stream.WriteAsync(ping.ToArray(), ct);
        Assert.Equal(Opcode.Pong, new PacketReader(await ReadFrameAsync(stream, ct)).Opcode);

        await stream.WriteAsync(new LoginRequestPacket { AccessToken = token }.Serialize(), ct);
        var loginReader = new PacketReader(await ReadFrameAsync(stream, ct));
        Assert.Equal(Opcode.LoginResult, loginReader.Opcode);
        Assert.True(loginReader.ReadBool(), loginReader.ReadString());
        loginReader.EnsureFullyRead();

        var gold = Player.InitialGold;
        var gem = Player.InitialGem;
        foreach (var boxId in boxIds)
        {
            var id = Guid.NewGuid();
            var request = Request(boxId, 2, id);
            // 요청을 두 번 나누어 Write한다. 실제 TCP 수신 경계는 운영체제가 결정한다.
            await stream.WriteAsync(request.AsMemory(0, 3), ct);
            await stream.WriteAsync(request.AsMemory(3), ct);
            var bytes = await ReadFrameAsync(stream, ct);
            var result = ReadResult(bytes, id, CurrencyBoxUsePacketStatus.Used);
            var snapshot = Assert.IsType<CurrencyBoxUsePacketSnapshot>(result);
            Assert.Equal(boxId, snapshot.ItemTemplateId);
            Assert.Equal(2, snapshot.UsedQuantity);
            Assert.Equal(8, snapshot.QuantityAfter);
            if (boxId < 3103)
            {
                Assert.Equal(200, snapshot.GrantedAmount);
            }
            else
            {
                Assert.InRange(snapshot.GrantedAmount, 40, 400);
            }

            var isGold = boxId is 3101 or 3103;
            Assert.Equal(isGold ? CurrencyType.Gold : CurrencyType.Gem, snapshot.CurrencyType);
            gold += isGold ? snapshot.GrantedAmount : 0;
            gem += isGold ? 0 : snapshot.GrantedAmount;
            Assert.Equal(gold, snapshot.CurrentGold);
            Assert.Equal(gem, snapshot.CurrentGem);

            await stream.WriteAsync(request, ct);
            Assert.Equal(bytes, await ReadFrameAsync(stream, ct));
            await stream.WriteAsync(Request(boxId, 1, id), ct);
            Assert.Null(ReadResult(await ReadFrameAsync(stream, ct), id, CurrencyBoxUsePacketStatus.IdempotencyConflict));
            await stream.WriteAsync(Request(boxId == 3101 ? 3102 : 3101, 2, id), ct);
            Assert.Null(ReadResult(await ReadFrameAsync(stream, ct), id, CurrencyBoxUsePacketStatus.IdempotencyConflict));

            // TCP 성공 이력도 HTTP 재전송에서 같은 결과로 복원되어야 한다.
            using var replay = await http.PostAsJsonAsync(
                $"/players/me/currency-boxes/{boxId}/use", new { quantity = 2, requestId = id }, ct);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync(ct));
            Assert.Equal(snapshot.GrantedAmount, replayJson.RootElement.GetProperty("grantedAmount").GetInt32());
            Assert.Equal(snapshot.CurrentGold, replayJson.RootElement.GetProperty("currentGold").GetInt32());
            Assert.Equal(snapshot.CurrentGem, replayJson.RootElement.GetProperty("currentGem").GetInt32());

            await using var db = new GameDbContext(options);
            var record = await db.CurrencyBoxUseRecords.SingleAsync(
                record => record.PlayerId == playerId && record.RequestId == id, ct);
            Assert.Equal(snapshot.GrantedAmount, record.GrantedAmount);
            Assert.Equal(snapshot.ProcessedAt, DateTimeOffset.FromUnixTimeMilliseconds(
                new DateTimeOffset(record.ProcessedAt).ToUnixTimeMilliseconds()).UtcDateTime);
        }

        foreach (var (itemId, quantity, status) in new[]
        {
            (3101, 9, CurrencyBoxUsePacketStatus.InsufficientQuantity),
            (1001, 1, CurrencyBoxUsePacketStatus.BoxNotFound)
        })
        {
            var id = Guid.NewGuid();
            await stream.WriteAsync(Request(itemId, quantity, id), ct);
            Assert.Null(ReadResult(await ReadFrameAsync(stream, ct), id, status));
        }

        await using var verify = new GameDbContext(options);
        var quantities = await verify.PlayerItems.Where(item => item.PlayerId == playerId)
            .Select(item => item.Quantity).ToArrayAsync(ct);
        Assert.Equal(4, quantities.Length);
        Assert.All(quantities, quantity => Assert.Equal(8, quantity));
        var player = await verify.Players.SingleAsync(player => player.Id == playerId, ct);
        Assert.Equal(gold, player.Gold);
        Assert.Equal(gem, player.Gem);
        Assert.Equal(4, await verify.CurrencyBoxUseRecords.CountAsync(record => record.PlayerId == playerId, ct));
        Assert.Equal(4, await verify.InventoryItemChangeLogs.CountAsync(log => log.PlayerId == playerId, ct));
        Assert.Equal(4, await verify.CurrencyChangeLogs.CountAsync(log => log.PlayerId == playerId, ct));
        Assert.Equal(4, await verify.RewardGrantRecords.CountAsync(record => record.PlayerId == playerId, ct));
    }

    private static byte[] Request(int itemId, int quantity, Guid requestId) =>
        new CurrencyBoxUseRequestPacket { ItemTemplateId = itemId, Quantity = quantity, RequestId = requestId }.Serialize();

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[2];
        await stream.ReadExactlyAsync(header, ct);
        var size = BinaryPrimitives.ReadUInt16LittleEndian(header);
        Assert.InRange((int)size, PacketReader.HeaderSize, 4096);
        var bytes = new byte[size];
        header.CopyTo(bytes, 0);
        await stream.ReadExactlyAsync(bytes.AsMemory(2), ct);
        return bytes;
    }

    private static CurrencyBoxUsePacketSnapshot? ReadResult(
        byte[] bytes, Guid requestId, CurrencyBoxUsePacketStatus expected)
    {
        var reader = new PacketReader(bytes);
        Assert.Equal(Opcode.CurrencyBoxUseResult, reader.Opcode);
        Assert.Equal((int)expected, reader.ReadInt());
        Assert.Equal(requestId.ToString("D"), reader.ReadString());
        var hasSnapshot = reader.ReadBool();
        Assert.Equal(expected == CurrencyBoxUsePacketStatus.Used, hasSnapshot);
        var snapshot = hasSnapshot
            ? new CurrencyBoxUsePacketSnapshot(
                reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), (CurrencyType)reader.ReadInt(),
                reader.ReadInt(), reader.ReadInt(), reader.ReadInt(),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadLong()).UtcDateTime)
            : null;
        reader.EnsureFullyRead();
        return snapshot;
    }
}
