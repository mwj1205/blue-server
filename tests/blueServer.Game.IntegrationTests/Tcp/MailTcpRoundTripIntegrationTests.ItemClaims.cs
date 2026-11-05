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
    public async Task MailFlow_PartialItemClaimPreservesRemainderAndReplaysRequest()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        using var http = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable(
                ApiPostgreSqlIntegrationFactAttribute.ApiBaseAddressEnvironmentVariable)!)
        };
        var nickname = $"mail-tcp-items-{Guid.NewGuid():N}";
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
        long mailId;
        int templateId;
        var sentAt = DateTime.UtcNow.AddMinutes(-5);
        sentAt = new DateTime(sentAt.Ticks - sentAt.Ticks % 10, DateTimeKind.Utc);
        var expiresAt = sentAt.AddDays(1);
        var readAt = sentAt.AddMinutes(1);
        var sourceId = $"tcp-items-{Guid.NewGuid():N}";
        await using (var db = new GameDbContext(options))
        {
            playerId = await db.Players.Where(player => player.Nickname == nickname)
                .Select(player => player.Id).SingleAsync(cancellationToken);
            templateId = await FindUnusedTcpItemTemplateIdAsync(db, cancellationToken);
            var code = $"tcp_item_{Guid.NewGuid():N}";
            var template = ItemTemplate.Create(
                templateId,
                code,
                $"item.{code}.name",
                $"item.{code}.description",
                ItemType.Material);
            db.ItemTemplates.Add(template);
            db.PlayerItems.Add(PlayerItem.Create(playerId, template, 999000));
            var mail = Mail.Create(
                playerId,
                "TCP item Mail",
                "Keep the original Mail",
                sentAt,
                expiresAt,
                [CurrencyReward.Create(RewardType.Gold, 100)],
                sourceId: sourceId,
                inventoryItemRewards: [InventoryItemReward.Create(templateId, 1000)]);
            mail.MarkAsRead(readAt);
            db.Mails.Add(mail);
            await db.SaveChangesAsync(cancellationToken);
            mailId = mail.Id;
        }

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(
            Environment.GetEnvironmentVariable(GameTcpPostgreSqlIntegrationFactAttribute.GameHostEnvironmentVariable)
                ?? "127.0.0.1",
            int.Parse(Environment.GetEnvironmentVariable(
                GameTcpPostgreSqlIntegrationFactAttribute.GamePortEnvironmentVariable)!),
            cancellationToken);
        await using var stream = tcp.GetStream();
        await WritePacketAsync(
            stream,
            new LoginRequestPacket
            {
                AccessToken = token
            }.Serialize(),
            cancellationToken);
        Assert.True(ReadLoginResult(await ReadPacketAsync(stream, cancellationToken)).Success);

        // 기존 요청은 아이템 Mail을 잘못 완료 처리하지 않고 거절한다.
        await WritePacketAsync(
            stream,
            new MailClaimRequestPacket
            {
                MailId = mailId
            }.Serialize(),
            cancellationToken);
        var legacy = ReadMailClaimResult(await ReadPacketAsync(stream, cancellationToken));
        Assert.False(legacy.Success);
        Assert.Equal(MailClaimPacketStatus.NoRewards, legacy.Status);

        var firstId = Guid.NewGuid();
        var first = await ClaimItemMailAsync(stream, mailId, firstId, cancellationToken);
        Assert.True(first.Success);
        Assert.Equal(MailClaimPacketStatus.PartiallyClaimed, first.Status);
        Assert.Equal(firstId, first.RequestId);
        Assert.NotNull(first.ProcessedAt);
        Assert.Null(first.ClaimedAt);
        Assert.Equal(100, first.GrantedGold);
        Assert.Equal(Player.InitialGold + 100, first.CurrentGold);
        Assert.Equal(Player.InitialGem, first.CurrentGem);
        Assert.Equal(new MailClaimPacketItem(templateId, 999, 1, 999999), Assert.Single(first.Items));

        var retry = await ClaimItemMailAsync(stream, mailId, firstId, cancellationToken);
        AssertSameItemClaimResult(first, retry);

        var fullId = Guid.NewGuid();
        var full = await ClaimItemMailAsync(stream, mailId, fullId, cancellationToken);
        Assert.True(full.Success);
        Assert.Equal(MailClaimPacketStatus.NoCapacity, full.Status);
        Assert.Null(full.ClaimedAt);
        Assert.Equal(0, full.GrantedGold);
        Assert.Equal(new MailClaimPacketItem(templateId, 0, 1, 999999), Assert.Single(full.Items));

        await using (var db = new GameDbContext(options))
        {
            var mail = await db.Mails.Include(mail => mail.ItemAttachments)
                .SingleAsync(mail => mail.Id == mailId, cancellationToken);
            Assert.Null(mail.ClaimedAt);
            Assert.Equal(expiresAt, mail.ExpiresAt);
            Assert.Equal(readAt, mail.ReadAt);
            Assert.Equal(999, Assert.Single(mail.ItemAttachments).ClaimedQuantity);
            var item = await db.PlayerItems.SingleAsync(
                item => item.PlayerId == playerId && item.ItemTemplateId == templateId,
                cancellationToken);
            Assert.True(item.TryDecrease(1));
            await db.SaveChangesAsync(cancellationToken);
        }

        // 공간이 생겨도 같은 요청은 재실행하지 않고 최초 결과를 반환한다.
        AssertSameItemClaimResult(
            full,
            await ClaimItemMailAsync(stream, mailId, fullId, cancellationToken));
        var complete = await ClaimItemMailAsync(stream, mailId, Guid.NewGuid(), cancellationToken);
        Assert.True(complete.Success);
        Assert.Equal(MailClaimPacketStatus.Claimed, complete.Status);
        Assert.NotNull(complete.ClaimedAt);
        Assert.Equal(0, complete.GrantedGold);
        Assert.Equal(Player.InitialGold + 100, complete.CurrentGold);
        Assert.Equal(new MailClaimPacketItem(templateId, 1, 0, 999999), Assert.Single(complete.Items));
        AssertSameItemClaimResult(
            first,
            await ClaimItemMailAsync(stream, mailId, firstId, cancellationToken));

        await using var finalDb = new GameDbContext(options);
        var finalMail = await finalDb.Mails.Include(mail => mail.ItemAttachments)
            .SingleAsync(mail => mail.Id == mailId, cancellationToken);
        Assert.Equal("TCP item Mail", finalMail.Title);
        Assert.Equal("Keep the original Mail", finalMail.Body);
        Assert.Equal(sentAt, finalMail.SentAt);
        Assert.Equal(expiresAt, finalMail.ExpiresAt);
        Assert.Equal(readAt, finalMail.ReadAt);
        Assert.Equal(sourceId, finalMail.SourceId);
        Assert.Equal(ToUnixMilliseconds(finalMail.ClaimedAt!.Value), complete.ClaimedAt);
        var attachment = Assert.Single(finalMail.ItemAttachments);
        Assert.Equal(1000, attachment.Quantity);
        Assert.Equal(1000, attachment.ClaimedQuantity);
        Assert.Equal(0, attachment.RemainingQuantity);
        Assert.Equal(3, await finalDb.MailClaimRecords.CountAsync(
            record => record.PlayerId == playerId, cancellationToken));
        Assert.Equal(1, await finalDb.RewardGrantRecords.CountAsync(
            record => record.PlayerId == playerId, cancellationToken));
        Assert.Equal(999999, await finalDb.PlayerItems.Where(item => item.PlayerId == playerId)
            .Select(item => item.Quantity).SingleAsync(cancellationToken));
        Assert.Equal(Player.InitialGold + 100, await finalDb.Players.Where(player => player.Id == playerId)
            .Select(player => player.Gold).SingleAsync(cancellationToken));
    }

    private static async Task<ItemClaimResult> ClaimItemMailAsync(
        NetworkStream stream,
        long mailId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        await WritePacketAsync(
            stream,
            new MailClaimRequestPacket
            {
                MailId = mailId,
                RequestId = requestId
            }.Serialize(),
            cancellationToken);
        var reader = new PacketReader(await ReadPacketAsync(stream, cancellationToken));
        Assert.Equal(Opcode.MailClaimResult, reader.Opcode);
        var success = reader.ReadBool();
        var status = ReadDefinedStatus<MailClaimPacketStatus>(reader);
        var message = reader.ReadString();
        var claimedAt = ReadOptionalUnixMilliseconds(reader);
        var gold = reader.ReadInt();
        var gem = reader.ReadInt();
        var id = Guid.ParseExact(reader.ReadString(), "D");
        Assert.Equal(requestId, id);
        var processedAt = ReadOptionalUnixMilliseconds(reader);
        var grantedGold = reader.ReadInt();
        var grantedGem = reader.ReadInt();
        var count = ReadBoundedCount(reader, 100, "item claim results");
        var items = new MailClaimPacketItem[count];
        for (var index = 0; index < count; index++)
        {
            items[index] = new MailClaimPacketItem(
                reader.ReadInt(),
                reader.ReadInt(),
                reader.ReadInt(),
                reader.ReadInt());
        }
        reader.EnsureFullyRead();
        return new ItemClaimResult(
            success,
            status,
            message,
            claimedAt,
            gold,
            gem,
            id,
            processedAt,
            grantedGold,
            grantedGem,
            items);
    }

    private static void AssertSameItemClaimResult(
        ItemClaimResult expected,
        ItemClaimResult actual)
    {
        Assert.Equal(expected with { Items = actual.Items }, actual);
        Assert.Equal(expected.Items, actual.Items);
    }

    private static async Task<int> FindUnusedTcpItemTemplateIdAsync(
        GameDbContext db,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var id = Random.Shared.Next(1, int.MaxValue);
            if (!await db.ItemTemplates.AnyAsync(template => template.Id == id, cancellationToken))
            {
                return id;
            }
        }
        throw new InvalidOperationException("Cannot allocate test ItemTemplate id.");
    }

    private sealed record ItemClaimResult(
        bool Success,
        MailClaimPacketStatus Status,
        string Message,
        long? ClaimedAt,
        int CurrentGold,
        int CurrentGem,
        Guid RequestId,
        long? ProcessedAt,
        int GrantedGold,
        int GrantedGem,
        IReadOnlyList<MailClaimPacketItem> Items);
}
