using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Domain.Rewards;
using blueServer.Game.Packets;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Tcp;

public sealed partial class MailTcpRoundTripIntegrationTests
{
    [GameTcpPostgreSqlIntegrationFact]
    public async Task MailFlow_ClaimAllReplaysSplitResponseAfterReconnectWithoutDoubleGrant()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var cancellationToken = timeout.Token;
        using var http = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable(
                ApiPostgreSqlIntegrationFactAttribute.ApiBaseAddressEnvironmentVariable)!)
        };
        var nickname = $"mail-tcp-batch-{Guid.NewGuid():N}";
        var credentials = new { nickname, password = "integration-test-password" };
        using var register = await http.PostAsJsonAsync(
            "/register",
            credentials,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var login = await http.PostAsJsonAsync(
            "/login",
            credentials,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = await ReadAccessTokenAsync(login, cancellationToken);

        var options = CreateDbContextOptions();
        long playerId;
        int templateId;
        var sentAt = DateTime.UtcNow.AddMinutes(-5);
        sentAt = new DateTime(sentAt.Ticks - sentAt.Ticks % 10, DateTimeKind.Utc);
        var expiresAt = sentAt.AddDays(30);
        var readAt = sentAt.AddMinutes(2);
        var sourcePrefix = $"tcp-batch-{Guid.NewGuid():N}";
        await using (var db = new GameDbContext(options))
        {
            playerId = await db.Players.Where(player => player.Nickname == nickname)
                .Select(player => player.Id).SingleAsync(cancellationToken);
            templateId = await FindUnusedTcpItemTemplateIdAsync(db, cancellationToken);
            var code = $"tcp_batch_{Guid.NewGuid():N}";
            var template = ItemTemplate.Create(
                templateId,
                code,
                $"item.{code}.name",
                $"item.{code}.description",
                ItemType.Material);
            db.ItemTemplates.Add(template);
            db.PlayerItems.Add(PlayerItem.Create(playerId, template, 999000));
            for (var index = 0; index < 100; index++)
            {
                var gold = index == 0 ? 100 : index == 1 ? 30 : 0;
                var quantity = index == 0 ? 500 : index == 1 ? 1000 : 1;
                var mail = Mail.Create(
                    playerId,
                    $"TCP batch Mail {index}",
                    "Preserve the Mail while collecting item rewards",
                    sentAt.AddSeconds(index),
                    expiresAt,
                    gold == 0 ? [] : [CurrencyReward.Create(RewardType.Gold, gold)],
                    sourceId: $"{sourcePrefix}-{index}",
                    inventoryItemRewards: [InventoryItemReward.Create(templateId, quantity)]);
                mail.MarkAsRead(readAt);
                db.Mails.Add(mail);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var requestId = Guid.NewGuid();
        var request = new MailClaimAllRequestPacket
        {
            RequestId = requestId
        }.Serialize();
        byte[] interruptedFirstFrame;
        using (var interrupted = await ConnectBatchTestClientAsync(token, cancellationToken))
        {
            await using var stream = interrupted.GetStream();
            await WritePacketAsync(stream, request, cancellationToken);
            interruptedFirstFrame = await ReadPacketAsync(stream, cancellationToken);
            var header = new PacketReader(interruptedFirstFrame);
            Assert.Equal(Opcode.MailClaimAllResult, header.Opcode);
            Assert.Equal(requestId.ToString("D"), header.ReadString());
            Assert.Equal(0, header.ReadInt());
            Assert.True(header.ReadInt() > 1);
            Assert.Equal(4096, interruptedFirstFrame.Length);
            // 첫 조각만 소비하고 연결을 닫는다. 전체 결과는 아직 클라이언트에 없다.
        }

        using var reconnected = await ConnectBatchTestClientAsync(token, cancellationToken);
        await using var replayStream = reconnected.GetStream();
        await WritePacketAsync(replayStream, request, cancellationToken);
        var replay = await ReadBatchFramesAsync(replayStream, requestId, cancellationToken);
        Assert.Equal(interruptedFirstFrame, replay.FirstFrame);
        Assert.True(replay.PartCount > 1);
        var result = ParseBatchPayload(replay.Payload);
        Assert.Equal(MailClaimAllPacketStatus.Processed, result.Status);
        Assert.NotNull(result.ProcessedAt);
        Assert.Equal(1, result.ClaimedMailCount);
        Assert.Equal(130, result.GrantedGold);
        Assert.Equal(0, result.GrantedGem);
        Assert.Equal(Player.InitialGold + 130, result.CurrentGold);
        Assert.Equal(Player.InitialGem, result.CurrentGem);
        Assert.False(result.HasMore);
        Assert.Equal(100, result.Entries.Count);
        Assert.Equal(MailClaimPacketStatus.Claimed, result.Entries[0].Status);
        Assert.NotNull(result.Entries[0].ClaimedAt);
        Assert.Equal(
            new MailClaimPacketItem(templateId, 500, 0, 999500),
            Assert.Single(result.Entries[0].Items));
        Assert.Equal(MailClaimPacketStatus.PartiallyClaimed, result.Entries[1].Status);
        Assert.Null(result.Entries[1].ClaimedAt);
        Assert.Equal(
            new MailClaimPacketItem(templateId, 499, 501, 999999),
            Assert.Single(result.Entries[1].Items));
        Assert.All(result.Entries.Skip(2), entry =>
        {
            Assert.Equal(MailClaimPacketStatus.NoCapacity, entry.Status);
            Assert.Null(entry.ClaimedAt);
            Assert.Equal(new MailClaimPacketItem(templateId, 0, 1, 999999), Assert.Single(entry.Items));
        });

        await WritePacketAsync(replayStream, request, cancellationToken);
        var repeated = await ReadBatchFramesAsync(replayStream, requestId, cancellationToken);
        Assert.Equal(replay.Payload, repeated.Payload);
        Assert.Equal(replay.PartCount, repeated.PartCount);

        await using var finalDb = new GameDbContext(options);
        var player = await finalDb.Players.SingleAsync(player => player.Id == playerId, cancellationToken);
        Assert.Equal(Player.InitialGold + 130, player.Gold);
        Assert.Equal(Player.InitialGem, player.Gem);
        Assert.Equal(999999, await finalDb.PlayerItems.Where(item => item.PlayerId == playerId)
            .Select(item => item.Quantity).SingleAsync(cancellationToken));
        var batch = await finalDb.MailClaimAllRecords.Include(batch => batch.Entries)
            .SingleAsync(batch => batch.PlayerId == playerId, cancellationToken);
        Assert.Equal(requestId, batch.RequestId);
        Assert.Equal(100, batch.Entries.Count);
        Assert.Equal(100, await finalDb.MailClaimRecords.CountAsync(record => record.PlayerId == playerId, cancellationToken));
        Assert.Equal(2, await finalDb.RewardGrantRecords.CountAsync(record => record.PlayerId == playerId, cancellationToken));
        Assert.Equal(2, await finalDb.InventoryItemChangeLogs.CountAsync(log => log.PlayerId == playerId, cancellationToken));
        var mails = await finalDb.Mails.Include(mail => mail.ItemAttachments)
            .Where(mail => mail.PlayerId == playerId).OrderBy(mail => mail.SentAt).ThenBy(mail => mail.Id)
            .ToArrayAsync(cancellationToken);
        Assert.Equal(100, mails.Length);
        for (var index = 0; index < mails.Length; index++)
        {
            var mail = mails[index];
            Assert.Equal(mail.Id, result.Entries[index].MailId);
            Assert.Equal($"TCP batch Mail {index}", mail.Title);
            Assert.Equal("Preserve the Mail while collecting item rewards", mail.Body);
            Assert.Equal($"{sourcePrefix}-{index}", mail.SourceId);
            Assert.Equal(sentAt.AddSeconds(index), mail.SentAt);
            Assert.Equal(expiresAt, mail.ExpiresAt);
            Assert.Equal(readAt, mail.ReadAt);
            Assert.Equal(index == 0, mail.ClaimedAt.HasValue);
            var item = Assert.Single(mail.ItemAttachments);
            Assert.Equal(index == 0 ? 500 : index == 1 ? 1000 : 1, item.Quantity);
            Assert.Equal(index == 0 ? 500 : index == 1 ? 499 : 0, item.ClaimedQuantity);
        }
    }

    private static async Task<TcpClient> ConnectBatchTestClientAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(
                Environment.GetEnvironmentVariable(GameTcpPostgreSqlIntegrationFactAttribute.GameHostEnvironmentVariable)
                    ?? "127.0.0.1",
                int.Parse(Environment.GetEnvironmentVariable(
                    GameTcpPostgreSqlIntegrationFactAttribute.GamePortEnvironmentVariable)!),
                cancellationToken);
            var stream = client.GetStream();
            await WritePacketAsync(
                stream,
                new LoginRequestPacket
                {
                    AccessToken = token
                }.Serialize(),
                cancellationToken);
            Assert.True(ReadLoginResult(await ReadPacketAsync(stream, cancellationToken)).Success);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<BatchFrames> ReadBatchFramesAsync(
        NetworkStream stream,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream();
        var partCount = 1;
        byte[] firstFrame = [];
        for (var index = 0; index < partCount; index++)
        {
            var frame = await ReadPacketAsync(stream, cancellationToken);
            var reader = new PacketReader(frame);
            Assert.Equal(Opcode.MailClaimAllResult, reader.Opcode);
            Assert.Equal(requestId.ToString("D"), reader.ReadString());
            Assert.Equal(index, reader.ReadInt());
            var receivedPartCount = reader.ReadInt();
            Assert.InRange(receivedPartCount, 1, 64);
            if (index == 0)
            {
                partCount = receivedPartCount;
                firstFrame = frame;
            }

            Assert.Equal(partCount, receivedPartCount);
            Assert.True(reader.RemainingBytes > 0);
            Assert.True(payload.Length + reader.RemainingBytes <= 256 * 1024);
            payload.Write(frame, frame.Length - reader.RemainingBytes, reader.RemainingBytes);
        }

        return new BatchFrames(payload.ToArray(), firstFrame, partCount);
    }

    private static BatchPayload ParseBatchPayload(byte[] payload)
    {
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream);
        var status = (MailClaimAllPacketStatus)reader.ReadInt32();
        var processedAt = ReadBatchOptionalTime(reader);
        var claimedCount = reader.ReadInt32();
        var grantedGold = reader.ReadInt32();
        var grantedGem = reader.ReadInt32();
        var currentGold = reader.ReadInt32();
        var currentGem = reader.ReadInt32();
        var hasMore = ReadBatchBoolean(reader);
        var entryCount = reader.ReadInt32();
        Assert.InRange(entryCount, 0, 100);
        var entries = new List<MailClaimAllPacketEntry>();
        for (var index = 0; index < entryCount; index++)
        {
            var mailId = reader.ReadInt64();
            var entryStatus = (MailClaimPacketStatus)reader.ReadInt32();
            var claimedAt = ReadBatchOptionalTime(reader);
            var gold = reader.ReadInt32();
            var gem = reader.ReadInt32();
            var itemCount = reader.ReadInt32();
            Assert.InRange(itemCount, 0, 1000);
            var items = new List<MailClaimPacketItem>();
            for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                items.Add(new MailClaimPacketItem(
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32()));
            }

            entries.Add(new MailClaimAllPacketEntry(
                mailId,
                entryStatus,
                claimedAt,
                gold,
                gem,
                items));
        }

        Assert.Equal(stream.Length, stream.Position);
        return new BatchPayload(
            status,
            processedAt,
            claimedCount,
            grantedGold,
            grantedGem,
            currentGold,
            currentGem,
            hasMore,
            entries);
    }

    private static bool ReadBatchBoolean(BinaryReader reader)
    {
        var value = reader.ReadByte();
        Assert.InRange(value, (byte)0, (byte)1);
        return value == 1;
    }

    private static DateTime? ReadBatchOptionalTime(BinaryReader reader) =>
        ReadBatchBoolean(reader)
            ? DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64()).UtcDateTime
            : null;

    private sealed record BatchFrames(byte[] Payload, byte[] FirstFrame, int PartCount);

    private sealed record BatchPayload(
        MailClaimAllPacketStatus Status,
        DateTime? ProcessedAt,
        int ClaimedMailCount,
        int GrantedGold,
        int GrantedGem,
        int CurrentGold,
        int CurrentGem,
        bool HasMore,
        IReadOnlyList<MailClaimAllPacketEntry> Entries);
}
