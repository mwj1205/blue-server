using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Game.Packets;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Mails;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Tcp;

public sealed class MailTcpRoundTripIntegrationTests
{
    private const int MaxPacketSize = 4096;

    [GameTcpPostgreSqlIntegrationFact]
    public async Task MailFlow_CompleteAuthenticatedTcpRoundTrip()
    {
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));
        var cancellationToken = timeoutCts.Token;
        var options = CreateDbContextOptions();
        var nickname = $"mail-tcp-{Guid.NewGuid():N}";
        const string password = "integration-test-password";
        var apiBaseAddress = Environment.GetEnvironmentVariable(
            ApiPostgreSqlIntegrationFactAttribute
                .ApiBaseAddressEnvironmentVariable)!;

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseAddress, UriKind.Absolute)
        };

        using var registerResponse = await httpClient.PostAsJsonAsync(
            "/register",
            new
            {
                nickname,
                password
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        using var loginResponse = await httpClient.PostAsJsonAsync(
            "/login",
            new
            {
                nickname,
                password
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var accessToken = await ReadAccessTokenAsync(
            loginResponse,
            cancellationToken);
        var fixture = await ArrangeMailAsync(
            options,
            nickname,
            cancellationToken);

        using var tcpClient = new TcpClient();
        var gameHost = Environment.GetEnvironmentVariable(
            GameTcpPostgreSqlIntegrationFactAttribute
                .GameHostEnvironmentVariable) ?? "127.0.0.1";
        var gamePort = int.Parse(
            Environment.GetEnvironmentVariable(
                GameTcpPostgreSqlIntegrationFactAttribute
                    .GamePortEnvironmentVariable)!);

        await tcpClient.ConnectAsync(
            gameHost,
            gamePort,
            cancellationToken);
        await using var stream = tcpClient.GetStream();

        await WritePacketAsync(
            stream,
            new LoginRequestPacket
            {
                AccessToken = accessToken
            }.Serialize(),
            cancellationToken);
        var loginResult = ReadLoginResult(await ReadPacketAsync(
            stream,
            cancellationToken));
        Assert.True(loginResult.Success, loginResult.Message);

        await WritePacketAsync(
            stream,
            new MailListRequestPacket
            {
                PageSize = 1
            }.Serialize(),
            cancellationToken);
        var firstPage = ReadMailListResult(await ReadPacketAsync(
            stream,
            cancellationToken));

        Assert.True(firstPage.Success, firstPage.Message);
        var newestItem = Assert.Single(firstPage.Items);
        Assert.Equal(fixture.NewestMailId, newestItem.Id);
        Assert.Equal("TCP newest Mail", newestItem.Title);
        Assert.False(newestItem.IsRead);
        Assert.False(newestItem.IsClaimed);
        Assert.False(newestItem.IsExpired);
        Assert.True(newestItem.CanClaim);
        Assert.Equal(2, newestItem.AttachmentCount);
        Assert.NotNull(firstPage.NextCursor);

        await WritePacketAsync(
            stream,
            new MailListRequestPacket
            {
                PageSize = 1,
                Cursor = new MailListCursor(
                    DateTimeOffset.FromUnixTimeMilliseconds(
                        firstPage.NextCursor!.SentAtUnixMilliseconds)
                        .UtcDateTime,
                    firstPage.NextCursor.Id)
            }.Serialize(),
            cancellationToken);
        var secondPage = ReadMailListResult(await ReadPacketAsync(
            stream,
            cancellationToken));

        Assert.True(secondPage.Success, secondPage.Message);
        var olderItem = Assert.Single(secondPage.Items);
        Assert.Equal(fixture.OlderMailId, olderItem.Id);
        Assert.Null(secondPage.NextCursor);

        await WritePacketAsync(
            stream,
            new MailDetailRequestPacket
            {
                MailId = fixture.NewestMailId
            }.Serialize(),
            cancellationToken);
        var detail = ReadMailDetailResult(await ReadPacketAsync(
            stream,
            cancellationToken));

        Assert.True(detail.Success, detail.Message);
        Assert.NotNull(detail.Mail);
        Assert.Equal(fixture.NewestMailId, detail.Mail.Id);
        Assert.Equal("TCP newest Mail", detail.Mail.Title);
        Assert.Equal("TCP Mail detail and attachment verification.", detail.Mail.Body);
        Assert.Null(detail.Mail.ReadAtUnixMilliseconds);
        Assert.Null(detail.Mail.ClaimedAtUnixMilliseconds);
        Assert.False(detail.Mail.IsExpired);
        Assert.True(detail.Mail.CanClaim);
        Assert.Collection(
            detail.Mail.Attachments.OrderBy(item => item.RewardType),
            item =>
            {
                Assert.Equal((int)RewardType.Gold, item.RewardType);
                Assert.Equal(120, item.Amount);
            },
            item =>
            {
                Assert.Equal((int)RewardType.Gem, item.RewardType);
                Assert.Equal(15, item.Amount);
            });

        await WritePacketAsync(
            stream,
            new MailDetailRequestPacket
            {
                MailId = fixture.OtherPlayerMailId
            }.Serialize(),
            cancellationToken);
        var otherPlayerDetail = ReadMailDetailResult(
            await ReadPacketAsync(stream, cancellationToken));

        Assert.False(otherPlayerDetail.Success);
        Assert.Equal("Mail not found", otherPlayerDetail.Message);
        Assert.Null(otherPlayerDetail.Mail);

        await WritePacketAsync(
            stream,
            new MailReadRequestPacket
            {
                MailId = fixture.OtherPlayerMailId
            }.Serialize(),
            cancellationToken);
        var otherPlayerRead = ReadMailReadResult(
            await ReadPacketAsync(stream, cancellationToken));

        Assert.False(otherPlayerRead.Success);
        Assert.Equal(MailReadPacketStatus.NotFound, otherPlayerRead.Status);
        Assert.Null(otherPlayerRead.ReadAtUnixMilliseconds);

        await WritePacketAsync(
            stream,
            new MailReadRequestPacket
            {
                MailId = fixture.NewestMailId
            }.Serialize(),
            cancellationToken);
        var firstRead = ReadMailReadResult(await ReadPacketAsync(
            stream,
            cancellationToken));

        Assert.True(firstRead.Success, firstRead.Message);
        Assert.Equal(MailReadPacketStatus.MarkedAsRead, firstRead.Status);
        Assert.NotNull(firstRead.ReadAtUnixMilliseconds);

        await WritePacketAsync(
            stream,
            new MailReadRequestPacket
            {
                MailId = fixture.NewestMailId
            }.Serialize(),
            cancellationToken);
        var repeatedRead = ReadMailReadResult(await ReadPacketAsync(
            stream,
            cancellationToken));

        Assert.True(repeatedRead.Success, repeatedRead.Message);
        Assert.Equal(MailReadPacketStatus.AlreadyRead, repeatedRead.Status);
        Assert.Equal(
            firstRead.ReadAtUnixMilliseconds,
            repeatedRead.ReadAtUnixMilliseconds);

        await WritePacketAsync(
            stream,
            new MailClaimRequestPacket
            {
                MailId = fixture.OtherPlayerMailId
            }.Serialize(),
            cancellationToken);
        var otherPlayerClaim = ReadMailClaimResult(
            await ReadPacketAsync(stream, cancellationToken));

        Assert.False(otherPlayerClaim.Success);
        Assert.Equal(
            MailClaimPacketStatus.NotFound,
            otherPlayerClaim.Status);
        Assert.Null(otherPlayerClaim.ClaimedAtUnixMilliseconds);

        await WritePacketAsync(
            stream,
            new MailClaimRequestPacket
            {
                MailId = fixture.NewestMailId
            }.Serialize(),
            cancellationToken);
        var firstClaim = ReadMailClaimResult(await ReadPacketAsync(
            stream,
            cancellationToken));

        Assert.True(firstClaim.Success, firstClaim.Message);
        Assert.Equal(MailClaimPacketStatus.Claimed, firstClaim.Status);
        Assert.NotNull(firstClaim.ClaimedAtUnixMilliseconds);
        Assert.Equal(Player.InitialGold + 120, firstClaim.CurrentGold);
        Assert.Equal(Player.InitialGem + 15, firstClaim.CurrentGem);

        await WritePacketAsync(
            stream,
            new MailClaimRequestPacket
            {
                MailId = fixture.NewestMailId
            }.Serialize(),
            cancellationToken);
        var repeatedClaim = ReadMailClaimResult(await ReadPacketAsync(
            stream,
            cancellationToken));

        Assert.True(repeatedClaim.Success, repeatedClaim.Message);
        Assert.Equal(
            MailClaimPacketStatus.AlreadyClaimed,
            repeatedClaim.Status);
        Assert.Equal(
            firstClaim.ClaimedAtUnixMilliseconds,
            repeatedClaim.ClaimedAtUnixMilliseconds);
        Assert.Equal(firstClaim.CurrentGold, repeatedClaim.CurrentGold);
        Assert.Equal(firstClaim.CurrentGem, repeatedClaim.CurrentGem);

        await WritePacketAsync(
            stream,
            new MailClaimAllRequestPacket().Serialize(),
            cancellationToken);
        var claimAll = ReadMailClaimAllResult(await ReadPacketAsync(
            stream,
            cancellationToken));

        Assert.True(claimAll.Success, claimAll.Message);
        Assert.Equal(MailClaimAllPacketStatus.Claimed, claimAll.Status);
        Assert.Equal(1, claimAll.ClaimedMailCount);
        Assert.Equal(10, claimAll.GrantedGold);
        Assert.Equal(0, claimAll.GrantedGem);
        Assert.Equal(Player.InitialGold + 130, claimAll.CurrentGold);
        Assert.Equal(Player.InitialGem + 15, claimAll.CurrentGem);
        Assert.False(claimAll.HasMore);

        await WritePacketAsync(
            stream,
            new MailClaimAllRequestPacket().Serialize(),
            cancellationToken);
        var repeatedClaimAll = ReadMailClaimAllResult(
            await ReadPacketAsync(stream, cancellationToken));

        Assert.True(repeatedClaimAll.Success, repeatedClaimAll.Message);
        Assert.Equal(
            MailClaimAllPacketStatus.NothingToClaim,
            repeatedClaimAll.Status);
        Assert.Equal(0, repeatedClaimAll.ClaimedMailCount);
        Assert.Equal(0, repeatedClaimAll.GrantedGold);
        Assert.Equal(0, repeatedClaimAll.GrantedGem);
        Assert.Equal(claimAll.CurrentGold, repeatedClaimAll.CurrentGold);
        Assert.Equal(claimAll.CurrentGem, repeatedClaimAll.CurrentGem);
        Assert.False(repeatedClaimAll.HasMore);

        await AssertPersistedStateAsync(
            options,
            fixture,
            firstRead.ReadAtUnixMilliseconds!.Value,
            firstClaim.ClaimedAtUnixMilliseconds!.Value,
            cancellationToken);
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

    private static async Task<string> ReadAccessTokenAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var body = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
        var accessToken = body.RootElement
            .GetProperty("accessToken")
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        return accessToken;
    }

    private static async Task<MailFixture> ArrangeMailAsync(
        DbContextOptions<GameDbContext> options,
        string nickname,
        CancellationToken cancellationToken)
    {
        await using var db = new GameDbContext(options);
        var playerId = await db.Players
            .Where(player => player.Nickname == nickname)
            .Select(player => player.Id)
            .SingleAsync(cancellationToken);
        var otherPlayer = Player.Create(
            $"mail-tcp-other-{Guid.NewGuid():N}",
            "integration-test-password");
        db.Players.Add(otherPlayer);
        await db.SaveChangesAsync(cancellationToken);

        var sentAt = DateTime.UtcNow.AddMinutes(-10);
        var deliveryService = new MailDeliveryService(db);
        var olderResult = await deliveryService.DeliverAsync(
            new MailDeliveryRequest(
                playerId,
                MailSourceType.Event,
                $"tcp-round-trip-older:{Guid.NewGuid():N}",
                "TCP older Mail",
                "TCP Mail pagination verification.",
                sentAt,
                sentAt.AddDays(1),
                [RewardItem.Create(RewardType.Gold, 10)]),
            cancellationToken);
        var newestResult = await deliveryService.DeliverAsync(
            new MailDeliveryRequest(
                playerId,
                MailSourceType.Event,
                $"tcp-round-trip-newest:{Guid.NewGuid():N}",
                "TCP newest Mail",
                "TCP Mail detail and attachment verification.",
                sentAt.AddMinutes(1),
                sentAt.AddDays(1),
                [
                    RewardItem.Create(RewardType.Gold, 120),
                    RewardItem.Create(RewardType.Gem, 15)
                ]),
            cancellationToken);
        var otherPlayerResult = await deliveryService.DeliverAsync(
            new MailDeliveryRequest(
                otherPlayer.Id,
                MailSourceType.Event,
                $"tcp-round-trip-other:{Guid.NewGuid():N}",
                "Other Player Mail",
                "This Mail must not be exposed.",
                sentAt.AddMinutes(2),
                sentAt.AddDays(1),
                [RewardItem.Create(RewardType.Gold, 999)]),
            cancellationToken);

        Assert.Equal(MailDeliveryStatus.Delivered, olderResult.Status);
        Assert.Equal(MailDeliveryStatus.Delivered, newestResult.Status);
        Assert.Equal(MailDeliveryStatus.Delivered, otherPlayerResult.Status);

        return new MailFixture(
            playerId,
            otherPlayer.Id,
            olderResult.MailId!.Value,
            newestResult.MailId!.Value,
            otherPlayerResult.MailId!.Value);
    }

    private static async Task AssertPersistedStateAsync(
        DbContextOptions<GameDbContext> options,
        MailFixture fixture,
        long expectedReadAtUnixMilliseconds,
        long expectedClaimedAtUnixMilliseconds,
        CancellationToken cancellationToken)
    {
        await using var db = new GameDbContext(options);
        var player = await db.Players
            .AsNoTracking()
            .SingleAsync(
                player => player.Id == fixture.PlayerId,
                cancellationToken);
        var otherPlayer = await db.Players
            .AsNoTracking()
            .SingleAsync(
                player => player.Id == fixture.OtherPlayerId,
                cancellationToken);
        var mails = await db.Mails
            .AsNoTracking()
            .Where(mail =>
                mail.Id == fixture.OlderMailId ||
                mail.Id == fixture.NewestMailId ||
                mail.Id == fixture.OtherPlayerMailId)
            .Select(mail => new
            {
                mail.Id,
                mail.ReadAt,
                mail.ClaimedAt
            })
            .ToDictionaryAsync(mail => mail.Id, cancellationToken);
        var grantReasons = await db.RewardGrantRecords
            .AsNoTracking()
            .Where(record =>
                record.PlayerId == fixture.PlayerId &&
                (record.Reason ==
                    $"Mail reward {fixture.OlderMailId}" ||
                 record.Reason ==
                    $"Mail reward {fixture.NewestMailId}"))
            .Select(record => record.Reason)
            .ToArrayAsync(cancellationToken);

        Assert.Equal(Player.InitialGold + 130, player.Gold);
        Assert.Equal(Player.InitialGem + 15, player.Gem);
        Assert.Equal(Player.InitialGold, otherPlayer.Gold);
        Assert.Equal(Player.InitialGem, otherPlayer.Gem);

        var newestMail = mails[fixture.NewestMailId];
        Assert.True(newestMail.ReadAt.HasValue);
        Assert.True(newestMail.ClaimedAt.HasValue);
        Assert.Equal(
            expectedReadAtUnixMilliseconds,
            ToUnixMilliseconds(newestMail.ReadAt.Value));
        Assert.Equal(
            expectedClaimedAtUnixMilliseconds,
            ToUnixMilliseconds(newestMail.ClaimedAt.Value));

        var olderMail = mails[fixture.OlderMailId];
        Assert.True(olderMail.ReadAt.HasValue);
        Assert.True(olderMail.ClaimedAt.HasValue);

        var otherPlayerMail = mails[fixture.OtherPlayerMailId];
        Assert.Null(otherPlayerMail.ReadAt);
        Assert.Null(otherPlayerMail.ClaimedAt);
        Assert.Contains(
            $"Mail reward {fixture.OlderMailId}",
            grantReasons);
        Assert.Contains(
            $"Mail reward {fixture.NewestMailId}",
            grantReasons);
        Assert.Equal(2, grantReasons.Length);
    }

    private static async Task WritePacketAsync(
        NetworkStream stream,
        byte[] packet,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(packet, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadPacketAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var sizeBytes = new byte[sizeof(ushort)];
        await stream.ReadExactlyAsync(sizeBytes, cancellationToken);
        var packetSize = BinaryPrimitives.ReadUInt16LittleEndian(sizeBytes);

        if (packetSize is < PacketReader.HeaderSize or > MaxPacketSize)
        {
            throw new InvalidDataException(
                $"Invalid TCP packet size: {packetSize}.");
        }

        var packet = new byte[packetSize];
        sizeBytes.CopyTo(packet, 0);
        await stream.ReadExactlyAsync(
            packet.AsMemory(sizeof(ushort)),
            cancellationToken);
        return packet;
    }

    private static LoginPacketResult ReadLoginResult(byte[] packet)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(Opcode.LoginResult, reader.Opcode);
        var result = new LoginPacketResult(
            reader.ReadBool(),
            reader.ReadString());
        reader.EnsureFullyRead();
        return result;
    }

    private static MailListPacketResult ReadMailListResult(byte[] packet)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(Opcode.MailListResult, reader.Opcode);
        var success = reader.ReadBool();
        var message = reader.ReadString();
        var itemCount = ReadBoundedCount(
            reader,
            MailListQueryService.MaxPageSize,
            "Mail list item");
        var items = new MailListPacketResultItem[itemCount];

        for (var index = 0; index < itemCount; index++)
        {
            items[index] = new MailListPacketResultItem(
                reader.ReadLong(),
                reader.ReadString(),
                reader.ReadLong(),
                ReadOptionalUnixMilliseconds(reader),
                reader.ReadBool(),
                reader.ReadBool(),
                reader.ReadBool(),
                reader.ReadBool(),
                reader.ReadInt());
        }

        MailListPacketCursor? cursor = null;

        if (reader.ReadBool())
        {
            cursor = new MailListPacketCursor(
                reader.ReadLong(),
                reader.ReadLong());
        }

        reader.EnsureFullyRead();
        return new MailListPacketResult(success, message, items, cursor);
    }

    private static MailDetailPacketResult ReadMailDetailResult(byte[] packet)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(Opcode.MailDetailResult, reader.Opcode);
        var success = reader.ReadBool();
        var message = reader.ReadString();

        if (!success)
        {
            reader.EnsureFullyRead();
            return new MailDetailPacketResult(false, message, null);
        }

        var id = reader.ReadLong();
        var title = reader.ReadString();
        var body = reader.ReadString();
        var sentAt = reader.ReadLong();
        var expiresAt = ReadOptionalUnixMilliseconds(reader);
        var readAt = ReadOptionalUnixMilliseconds(reader);
        var claimedAt = ReadOptionalUnixMilliseconds(reader);
        var isExpired = reader.ReadBool();
        var canClaim = reader.ReadBool();
        var attachmentCount = ReadBoundedCount(
            reader,
            100,
            "Mail attachment");
        var attachments = new MailAttachmentPacketResultItem[attachmentCount];

        for (var index = 0; index < attachmentCount; index++)
        {
            attachments[index] = new MailAttachmentPacketResultItem(
                reader.ReadInt(),
                reader.ReadInt());
        }

        reader.EnsureFullyRead();
        return new MailDetailPacketResult(
            true,
            message,
            new MailDetailPacketResultItem(
                id,
                title,
                body,
                sentAt,
                expiresAt,
                readAt,
                claimedAt,
                isExpired,
                canClaim,
                attachments));
    }

    private static MailReadPacketResult ReadMailReadResult(byte[] packet)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(Opcode.MailReadResult, reader.Opcode);
        var success = reader.ReadBool();
        var status = ReadDefinedStatus<MailReadPacketStatus>(reader);
        var message = reader.ReadString();
        var readAt = ReadOptionalUnixMilliseconds(reader);

        reader.EnsureFullyRead();
        return new MailReadPacketResult(
            success,
            status,
            message,
            readAt);
    }

    private static MailClaimPacketResult ReadMailClaimResult(byte[] packet)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(Opcode.MailClaimResult, reader.Opcode);
        var success = reader.ReadBool();
        var status = ReadDefinedStatus<MailClaimPacketStatus>(reader);
        var message = reader.ReadString();
        var claimedAt = ReadOptionalUnixMilliseconds(reader);
        var currentGold = reader.ReadInt();
        var currentGem = reader.ReadInt();

        reader.EnsureFullyRead();
        return new MailClaimPacketResult(
            success,
            status,
            message,
            claimedAt,
            currentGold,
            currentGem);
    }

    private static MailClaimAllPacketResult ReadMailClaimAllResult(
        byte[] packet)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(Opcode.MailClaimAllResult, reader.Opcode);
        var success = reader.ReadBool();
        var status = ReadDefinedStatus<MailClaimAllPacketStatus>(reader);
        var message = reader.ReadString();
        var claimedMailCount = reader.ReadInt();
        var grantedGold = reader.ReadInt();
        var grantedGem = reader.ReadInt();
        var currentGold = reader.ReadInt();
        var currentGem = reader.ReadInt();
        var hasMore = reader.ReadBool();

        reader.EnsureFullyRead();
        return new MailClaimAllPacketResult(
            success,
            status,
            message,
            claimedMailCount,
            grantedGold,
            grantedGem,
            currentGold,
            currentGem,
            hasMore);
    }

    private static TStatus ReadDefinedStatus<TStatus>(PacketReader reader)
        where TStatus : struct, Enum
    {
        var statusValue = reader.ReadInt();

        if (!Enum.IsDefined(typeof(TStatus), statusValue))
        {
            throw new InvalidDataException(
                $"Undefined {typeof(TStatus).Name}: {statusValue}.");
        }

        return (TStatus)Enum.ToObject(typeof(TStatus), statusValue);
    }

    private static int ReadBoundedCount(
        PacketReader reader,
        int maximum,
        string fieldName)
    {
        var count = reader.ReadInt();

        if (count is < 0 || count > maximum)
        {
            throw new InvalidDataException(
                $"{fieldName} count is outside the supported range: {count}.");
        }

        return count;
    }

    private static long? ReadOptionalUnixMilliseconds(PacketReader reader)
    {
        return reader.ReadBool()
            ? reader.ReadLong()
            : null;
    }

    private static long ToUnixMilliseconds(DateTime value)
    {
        return new DateTimeOffset(value).ToUnixTimeMilliseconds();
    }

    private sealed record MailFixture(
        long PlayerId,
        long OtherPlayerId,
        long OlderMailId,
        long NewestMailId,
        long OtherPlayerMailId);

    private sealed record LoginPacketResult(
        bool Success,
        string Message);

    private sealed record MailListPacketResult(
        bool Success,
        string Message,
        IReadOnlyList<MailListPacketResultItem> Items,
        MailListPacketCursor? NextCursor);

    private sealed record MailListPacketResultItem(
        long Id,
        string Title,
        long SentAtUnixMilliseconds,
        long? ExpiresAtUnixMilliseconds,
        bool IsRead,
        bool IsClaimed,
        bool IsExpired,
        bool CanClaim,
        int AttachmentCount);

    private sealed record MailListPacketCursor(
        long SentAtUnixMilliseconds,
        long Id);

    private sealed record MailDetailPacketResult(
        bool Success,
        string Message,
        MailDetailPacketResultItem? Mail);

    private sealed record MailDetailPacketResultItem(
        long Id,
        string Title,
        string Body,
        long SentAtUnixMilliseconds,
        long? ExpiresAtUnixMilliseconds,
        long? ReadAtUnixMilliseconds,
        long? ClaimedAtUnixMilliseconds,
        bool IsExpired,
        bool CanClaim,
        IReadOnlyList<MailAttachmentPacketResultItem> Attachments);

    private sealed record MailAttachmentPacketResultItem(
        int RewardType,
        int Amount);

    private sealed record MailReadPacketResult(
        bool Success,
        MailReadPacketStatus Status,
        string Message,
        long? ReadAtUnixMilliseconds);

    private sealed record MailClaimPacketResult(
        bool Success,
        MailClaimPacketStatus Status,
        string Message,
        long? ClaimedAtUnixMilliseconds,
        int CurrentGold,
        int CurrentGem);

    private sealed record MailClaimAllPacketResult(
        bool Success,
        MailClaimAllPacketStatus Status,
        string Message,
        int ClaimedMailCount,
        int GrantedGold,
        int GrantedGem,
        int CurrentGold,
        int CurrentGem,
        bool HasMore);
}
