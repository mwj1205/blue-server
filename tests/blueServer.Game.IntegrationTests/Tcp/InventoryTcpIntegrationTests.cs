using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Game.Packets;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Tcp;

public sealed class InventoryTcpIntegrationTests
{
    [GameTcpPostgreSqlIntegrationFact]
    public async Task Inventory_ReturnsWholeAuthenticatedSnapshotAndRefreshesCurrentQuantities()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        using var http = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable(
                ApiPostgreSqlIntegrationFactAttribute.ApiBaseAddressEnvironmentVariable)!)
        };
        var nickname = $"inventory-tcp-{Guid.NewGuid():N}";
        var credentials = new { nickname, password = "integration-test-password" };
        using var register = await http.PostAsJsonAsync("/register", credentials, ct);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var login = await http.PostAsJsonAsync("/login", credentials, ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync(ct));
        var token = loginJson.RootElement.GetProperty("accessToken").GetString()!;

        using var client = new TcpClient();
        await client.ConnectAsync(
            Environment.GetEnvironmentVariable(GameTcpPostgreSqlIntegrationFactAttribute.GameHostEnvironmentVariable) ?? "127.0.0.1",
            int.Parse(Environment.GetEnvironmentVariable(GameTcpPostgreSqlIntegrationFactAttribute.GamePortEnvironmentVariable)!),
            ct);
        await using var stream = client.GetStream();

        // 미인증 조회는 응답 없이 거절하고, 뒤따르는 Ping은 정상 처리해야 한다.
        await stream.WriteAsync(new InventoryListRequestPacket(Guid.NewGuid()).Serialize(), ct);
        var ping = new PacketWriter();
        ping.WriteUShort(4);
        ping.WriteUShort((ushort)Opcode.Ping);
        await stream.WriteAsync(ping.ToArray(), ct);
        var pong = new PacketReader(await ReadFrameAsync(stream, ct));
        Assert.Equal(Opcode.Pong, pong.Opcode);
        pong.EnsureFullyRead();

        await stream.WriteAsync(new LoginRequestPacket { AccessToken = token }.Serialize(), ct);
        var authenticated = new PacketReader(await ReadFrameAsync(stream, ct));
        Assert.Equal(Opcode.LoginResult, authenticated.Opcode);
        Assert.True(authenticated.ReadBool(), authenticated.ReadString());
        authenticated.EnsureFullyRead();

        var requestId = Guid.NewGuid();
        var empty = await QueryAsync(stream, requestId, ct);
        Assert.Empty(empty);

        var options = new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable(
                PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!).Options;
        long playerId;
        var expected = new List<InventoryListPacketItem>();
        await using (var db = new GameDbContext(options))
        {
            playerId = await db.Players.Where(player => player.Nickname == nickname)
                .Select(player => player.Id).SingleAsync(ct);
            var other = Player.Create($"inventory-tcp-other-{Guid.NewGuid():N}");
            db.Players.Add(other);
            var start = Random.Shared.Next(100_000_000, 1_000_000_000);
            while (await db.ItemTemplates.AnyAsync(
                item => item.Id >= start && item.Id < start + 313, ct))
            {
                start += 313;
            }
            var templates = Enumerable.Range(start, 313).Select(id =>
            {
                var code = $"tcp_{Guid.NewGuid():N}";
                return ItemTemplate.Create(id, code, $"item.{code}.name", $"item.{code}.description",
                    (ItemType)((id - start) % 4 + 1));
            }).ToArray();
            templates[310].Deactivate();
            db.ItemTemplates.AddRange(templates);
            await db.SaveChangesAsync(ct);

            // 311종으로 310종 경계를 넘어 실제 두 프레임을 수신한다.
            foreach (var template in templates.Take(311))
            {
                db.PlayerItems.Add(PlayerItem.Create(playerId, template, 5));
                expected.Add(new(template.Id, 5, template.Type, template.IsActive));
            }
            var zero = PlayerItem.Create(playerId, templates[311], 1);
            Assert.True(zero.TryDecrease(1));
            db.PlayerItems.Add(zero);
            db.PlayerItems.Add(PlayerItem.Create(other.Id, templates[312], 999));
            await db.SaveChangesAsync(ct);
        }

        Assert.Equal(expected, await QueryAsync(stream, requestId, ct));
        await using (var db = new GameDbContext(options))
        {
            var item = await db.PlayerItems.SingleAsync(
                item => item.PlayerId == playerId && item.ItemTemplateId == expected[0].ItemTemplateId, ct);
            Assert.True(item.TryDecrease(3));
            await db.SaveChangesAsync(ct);
        }
        expected[0] = expected[0] with { Quantity = 2 };
        // 조회 RequestId는 결과 식별용이다. 같은 값이어도 이전 결과를 재생하지 않는다.
        Assert.Equal(expected, await QueryAsync(stream, requestId, ct));

        await using var verify = new GameDbContext(options);
        Assert.False(await verify.InventoryItemChangeLogs.AnyAsync(log => log.PlayerId == playerId, ct));
        var player = await verify.Players.SingleAsync(player => player.Id == playerId, ct);
        Assert.Equal(Player.InitialGold, player.Gold);
        Assert.Equal(Player.InitialGem, player.Gem);
    }

    private static async Task<IReadOnlyList<InventoryListPacketItem>> QueryAsync(
        NetworkStream stream,
        Guid requestId,
        CancellationToken ct)
    {
        var request = new InventoryListRequestPacket(requestId).Serialize();
        // Write 경계를 나누더라도 수신 TCP 세그먼트 경계까지 강제되는 것은 아니다.
        await stream.WriteAsync(request.AsMemory(0, 3), ct);
        await stream.WriteAsync(request.AsMemory(3), ct);
        var items = new List<InventoryListPacketItem>();
        var expectedParts = 1;
        var totalCount = 0;
        for (var partIndex = 0; partIndex < expectedParts; partIndex++)
        {
            var reader = new PacketReader(await ReadFrameAsync(stream, ct));
            Assert.Equal(Opcode.InventoryListResult, reader.Opcode);
            Assert.Equal(requestId.ToString("D"), reader.ReadString());
            Assert.Equal((int)InventoryListPacketStatus.Success, reader.ReadInt());
            Assert.Equal(partIndex, reader.ReadInt());
            var partCount = reader.ReadInt();
            var itemCount = reader.ReadInt();
            Assert.InRange(partCount, 1, 33);
            Assert.InRange(itemCount, 0, 10000);
            if (partIndex == 0)
            {
                totalCount = itemCount;
                expectedParts = Math.Max(1, (totalCount + 309) / 310);
            }
            Assert.Equal(expectedParts, partCount);
            Assert.Equal(totalCount, itemCount);
            var count = reader.ReadInt();
            Assert.Equal(Math.Min(310, totalCount - items.Count), count);
            for (var index = 0; index < count; index++)
            {
                items.Add(new(reader.ReadInt(), reader.ReadInt(), (ItemType)reader.ReadInt(), reader.ReadBool()));
            }
            reader.EnsureFullyRead();
        }
        Assert.Equal(totalCount, items.Count);
        return items;
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[2];
        await stream.ReadExactlyAsync(header, ct);
        var size = BinaryPrimitives.ReadUInt16LittleEndian(header);
        Assert.InRange((int)size, PacketReader.HeaderSize, 4096);
        var frame = new byte[size];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(2), ct);
        return frame;
    }
}
