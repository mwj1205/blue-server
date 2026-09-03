using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Mail;
using BlueServer.Client.Models;
using BlueServer.Client.Network;
using NUnit.Framework;

namespace BlueServer.Client.Tests
{
    public sealed class MailInboxCommandStateTests
    {
        private const long MailId = 11;

        private static readonly DateTime SentAt =
            new DateTime(2026, 9, 3, 4, 0, 0, DateTimeKind.Utc);

        [Test]
        public void MarkAsReadAsync_UpdatesListAndSelectedDetail()
        {
            var readAt = SentAt.AddMinutes(1);
            var client = CreateLoadedClient();
            client.ReadHandler = (mailId, cancellationToken) =>
                Task.FromResult(new MailReadResponse(
                    true,
                    MailReadStatus.MarkedAsRead,
                    "Mail marked as read",
                    readAt));
            var state = CreateLoadedState(client);

            var completed = state
                .MarkAsReadAsync(MailId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(completed, Is.True);
            Assert.That(state.Items[0].IsRead, Is.True);
            Assert.That(state.SelectedMail.ReadAt, Is.EqualTo(readAt));
            Assert.That(
                state.LastReadResult.Status,
                Is.EqualTo(MailReadStatus.MarkedAsRead));
            Assert.That(state.RequiresRefresh, Is.False);
        }

        [Test]
        public void ClaimAsync_UpdatesMailStateAndCurrentBalance()
        {
            var claimedAt = SentAt.AddMinutes(2);
            var client = CreateLoadedClient();
            client.ClaimHandler = (mailId, cancellationToken) =>
                Task.FromResult(new MailClaimResponse(
                    true,
                    MailClaimStatus.Claimed,
                    "Mail rewards claimed",
                    claimedAt,
                    1500,
                    700));
            var state = CreateLoadedState(client);

            var completed = state
                .ClaimAsync(MailId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(completed, Is.True);
            Assert.That(state.Items[0].IsRead, Is.True);
            Assert.That(state.Items[0].IsClaimed, Is.True);
            Assert.That(state.Items[0].CanClaim, Is.False);
            Assert.That(state.SelectedMail.ReadAt, Is.EqualTo(claimedAt));
            Assert.That(state.SelectedMail.ClaimedAt, Is.EqualTo(claimedAt));
            Assert.That(state.SelectedMail.CanClaim, Is.False);
            Assert.That(state.CurrentGold, Is.EqualTo(1500));
            Assert.That(state.CurrentGem, Is.EqualTo(700));
            Assert.That(state.HasCurrentBalance, Is.True);
        }

        [Test]
        public void ClaimAllAsync_RequiresListRefreshWithoutGuessingMailIds()
        {
            var client = CreateLoadedClient();
            client.ClaimAllHandler = cancellationToken =>
                Task.FromResult(new MailClaimAllResponse(
                    true,
                    MailClaimAllStatus.Claimed,
                    "Mail rewards claimed",
                    1,
                    1000,
                    500,
                    2000,
                    1000,
                    false));
            var state = CreateLoadedState(client);

            var completed = state
                .ClaimAllAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(completed, Is.True);
            Assert.That(state.Items[0].IsClaimed, Is.False);
            Assert.That(state.RequiresRefresh, Is.True);
            Assert.That(state.CurrentGold, Is.EqualTo(2000));
            Assert.That(state.CurrentGem, Is.EqualTo(1000));
            Assert.That(state.LastClaimAllResult.ClaimedMailCount, Is.EqualTo(1));
        }

        [Test]
        public void ClaimConcurrencyConflict_PreservesMailAndRequiresRefresh()
        {
            var client = CreateLoadedClient();
            client.ClaimHandler = (mailId, cancellationToken) =>
                Task.FromResult(new MailClaimResponse(
                    false,
                    MailClaimStatus.ConcurrencyConflict,
                    "Mail state changed",
                    null,
                    0,
                    0));
            var state = CreateLoadedState(client);

            var completed = state
                .ClaimAsync(MailId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(completed, Is.False);
            Assert.That(state.Items[0].IsClaimed, Is.False);
            Assert.That(state.RequiresRefresh, Is.True);
            Assert.That(state.Status, Is.EqualTo(MailInboxStatus.Failed));
            Assert.That(state.ErrorMessage, Is.EqualTo("Mail state changed"));
        }

        [Test]
        public void LostClaimResponse_MarksStateDisconnectedAndUncertain()
        {
            var client = CreateLoadedClient();
            client.ClaimHandler = (mailId, cancellationToken) =>
                CreateFailedTask<MailClaimResponse>(
                    new IOException("Connection closed"));
            var state = CreateLoadedState(client);

            Assert.Throws<IOException>(() => state
                .ClaimAsync(MailId, CancellationToken.None)
                .GetAwaiter()
                .GetResult());

            Assert.That(
                state.Status,
                Is.EqualTo(MailInboxStatus.Disconnected));
            Assert.That(state.RequiresRefresh, Is.True);
            Assert.That(state.Items[0].IsClaimed, Is.False);
        }

        private static MailInboxState CreateLoadedState(
            FakeMailClient client)
        {
            var state = new MailInboxState(client);

            state.LoadFirstPageAsync(20, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            state.LoadDetailAsync(MailId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return state;
        }

        private static FakeMailClient CreateLoadedClient()
        {
            return new FakeMailClient
            {
                ListHandler = (pageSize, cursor, cancellationToken) =>
                    Task.FromResult(new MailListResponse(
                        true,
                        "Mail list loaded",
                        new[]
                        {
                            new MailListItem(
                                MailId,
                                "Maintenance reward",
                                SentAt,
                                null,
                                false,
                                false,
                                false,
                                true,
                                2)
                        },
                        null)),
                DetailHandler = (mailId, cancellationToken) =>
                    Task.FromResult(new MailDetailResponse(
                        true,
                        "Mail detail loaded",
                        new MailDetail(
                            mailId,
                            "Maintenance reward",
                            "Thank you for your patience.",
                            SentAt,
                            null,
                            null,
                            null,
                            false,
                            true,
                            new[]
                            {
                                new MailAttachment(1, 1000),
                                new MailAttachment(2, 500)
                            })))
            };
        }

        private static Task<T> CreateFailedTask<T>(Exception exception)
        {
            var source = new TaskCompletionSource<T>();
            source.SetException(exception);
            return source.Task;
        }

        private sealed class FakeMailClient :
            IMailQueryClient,
            IMailCommandClient
        {
            public Func<
                int,
                MailListCursor,
                CancellationToken,
                Task<MailListResponse>> ListHandler { get; set; }

            public Func<
                long,
                CancellationToken,
                Task<MailDetailResponse>> DetailHandler { get; set; }

            public Func<
                long,
                CancellationToken,
                Task<MailReadResponse>> ReadHandler { get; set; }

            public Func<
                long,
                CancellationToken,
                Task<MailClaimResponse>> ClaimHandler { get; set; }

            public Func<
                CancellationToken,
                Task<MailClaimAllResponse>> ClaimAllHandler { get; set; }

            public Task<MailListResponse> GetMailListAsync(
                int pageSize,
                MailListCursor cursor,
                CancellationToken cancellationToken)
            {
                return ListHandler(pageSize, cursor, cancellationToken);
            }

            public Task<MailDetailResponse> GetMailDetailAsync(
                long mailId,
                CancellationToken cancellationToken)
            {
                return DetailHandler(mailId, cancellationToken);
            }

            public Task<MailReadResponse> MarkMailAsReadAsync(
                long mailId,
                CancellationToken cancellationToken)
            {
                return ReadHandler(mailId, cancellationToken);
            }

            public Task<MailClaimResponse> ClaimMailAsync(
                long mailId,
                CancellationToken cancellationToken)
            {
                return ClaimHandler(mailId, cancellationToken);
            }

            public Task<MailClaimAllResponse> ClaimAllMailAsync(
                CancellationToken cancellationToken)
            {
                return ClaimAllHandler(cancellationToken);
            }
        }
    }
}
