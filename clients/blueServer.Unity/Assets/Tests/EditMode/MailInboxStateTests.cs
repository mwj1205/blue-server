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
    public sealed class MailInboxStateTests
    {
        private static readonly DateTime SentAt =
            new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);

        [Test]
        public void LoadFirstPageAsync_ReplacesInboxAndStoresCursor()
        {
            var nextCursor = new MailListCursor(SentAt, 2);
            var client = new FakeMailQueryClient
            {
                ListHandler = (pageSize, cursor, cancellationToken) =>
                    Task.FromResult(new MailListResponse(
                        true,
                        "Mail list loaded",
                        new[] { CreateListItem(2) },
                        nextCursor))
            };
            var state = new MailInboxState(client);
            var changeCount = 0;
            state.Changed += () => changeCount++;

            var loaded = state
                .LoadFirstPageAsync(20, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(loaded, Is.True);
            Assert.That(state.Status, Is.EqualTo(MailInboxStatus.Loaded));
            Assert.That(state.Items.Count, Is.EqualTo(1));
            Assert.That(state.Items[0].Id, Is.EqualTo(2));
            Assert.That(state.NextCursor, Is.SameAs(nextCursor));
            Assert.That(state.HasMore, Is.True);
            Assert.That(state.IsEmpty, Is.False);
            Assert.That(state.ErrorMessage, Is.Null);
            Assert.That(changeCount, Is.EqualTo(2));
        }

        [Test]
        public void LoadNextPageAsync_AppendsOnlyNewMail()
        {
            var calls = 0;
            var firstCursor = new MailListCursor(SentAt, 2);
            var client = new FakeMailQueryClient
            {
                ListHandler = (pageSize, cursor, cancellationToken) =>
                {
                    calls++;

                    if (calls == 1)
                    {
                        return Task.FromResult(new MailListResponse(
                            true,
                            "First page",
                            new[] { CreateListItem(2) },
                            firstCursor));
                    }

                    Assert.That(cursor, Is.SameAs(firstCursor));
                    return Task.FromResult(new MailListResponse(
                        true,
                        "Last page",
                        new[]
                        {
                            CreateListItem(2),
                            CreateListItem(1)
                        },
                        null));
                }
            };
            var state = new MailInboxState(client);

            state.LoadFirstPageAsync(20, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var loaded = state
                .LoadNextPageAsync(20, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(loaded, Is.True);
            Assert.That(state.Items.Count, Is.EqualTo(2));
            Assert.That(state.Items[0].Id, Is.EqualTo(2));
            Assert.That(state.Items[1].Id, Is.EqualTo(1));
            Assert.That(state.HasMore, Is.False);
        }

        [Test]
        public void LoadDetailAsync_StoresSelectedMail()
        {
            var detail = CreateDetail(3);
            var client = new FakeMailQueryClient
            {
                DetailHandler = (mailId, cancellationToken) =>
                    Task.FromResult(new MailDetailResponse(
                        true,
                        "Mail detail loaded",
                        detail))
            };
            var state = new MailInboxState(client);

            var loaded = state
                .LoadDetailAsync(3, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(loaded, Is.True);
            Assert.That(state.SelectedMail, Is.SameAs(detail));
            Assert.That(state.Status, Is.EqualTo(MailInboxStatus.Loaded));
        }

        [Test]
        public void RequestWhileLoading_DoesNotStartAnotherRequest()
        {
            var responseSource =
                new TaskCompletionSource<MailListResponse>();
            var detailCallCount = 0;
            var client = new FakeMailQueryClient
            {
                ListHandler = (pageSize, cursor, cancellationToken) =>
                    responseSource.Task,
                DetailHandler = (mailId, cancellationToken) =>
                {
                    detailCallCount++;
                    return Task.FromResult(new MailDetailResponse(
                        true,
                        "Mail detail loaded",
                        CreateDetail(mailId)));
                }
            };
            var state = new MailInboxState(client);

            var firstRequest = state.LoadFirstPageAsync(
                20,
                CancellationToken.None);
            var secondRequest = state.LoadDetailAsync(
                1,
                CancellationToken.None);

            Assert.That(state.IsBusy, Is.True);
            Assert.That(
                secondRequest.GetAwaiter().GetResult(),
                Is.False);
            Assert.That(detailCallCount, Is.EqualTo(0));

            responseSource.SetResult(new MailListResponse(
                true,
                "Mail list loaded",
                new MailListItem[0],
                null));

            Assert.That(
                firstRequest.GetAwaiter().GetResult(),
                Is.True);
            Assert.That(state.IsEmpty, Is.True);
        }

        [Test]
        public void ServerFailure_SetsFailedStateWithoutReplacingItems()
        {
            var calls = 0;
            var client = new FakeMailQueryClient
            {
                ListHandler = (pageSize, cursor, cancellationToken) =>
                {
                    calls++;
                    return Task.FromResult(calls == 1
                        ? new MailListResponse(
                            true,
                            "Mail list loaded",
                            new[] { CreateListItem(1) },
                            null)
                        : new MailListResponse(
                            false,
                            "Mail list unavailable",
                            new MailListItem[0],
                            null));
                }
            };
            var state = new MailInboxState(client);

            state.LoadFirstPageAsync(20, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var loaded = state
                .LoadFirstPageAsync(20, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(loaded, Is.False);
            Assert.That(state.Status, Is.EqualTo(MailInboxStatus.Failed));
            Assert.That(state.ErrorMessage, Is.EqualTo("Mail list unavailable"));
            Assert.That(state.Items.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConnectionFailure_SetsDisconnectedStateAndRethrows()
        {
            var client = new FakeMailQueryClient
            {
                ListHandler = (pageSize, cursor, cancellationToken) =>
                    CreateFailedTask<MailListResponse>(
                        new IOException("Connection closed"))
            };
            var state = new MailInboxState(client);

            Assert.Throws<IOException>(() => state
                .LoadFirstPageAsync(20, CancellationToken.None)
                .GetAwaiter()
                .GetResult());

            Assert.That(
                state.Status,
                Is.EqualTo(MailInboxStatus.Disconnected));
            Assert.That(state.ErrorMessage, Is.EqualTo("Connection closed"));
        }

        private static MailListItem CreateListItem(long id)
        {
            return new MailListItem(
                id,
                "Mail " + id,
                SentAt,
                null,
                false,
                false,
                false,
                true,
                1);
        }

        private static MailDetail CreateDetail(long id)
        {
            return new MailDetail(
                id,
                "Mail " + id,
                "Body",
                SentAt,
                null,
                null,
                null,
                false,
                true,
                new[] { new MailAttachment(1, 100) });
        }

        private static Task<T> CreateFailedTask<T>(Exception exception)
        {
            var source = new TaskCompletionSource<T>();
            source.SetException(exception);
            return source.Task;
        }

        private sealed class FakeMailQueryClient : IMailQueryClient
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

            public Task<MailListResponse> GetMailListAsync(
                int pageSize,
                MailListCursor cursor,
                CancellationToken cancellationToken)
            {
                if (ListHandler == null)
                {
                    throw new InvalidOperationException(
                        "Mail list handler is not configured.");
                }

                return ListHandler(pageSize, cursor, cancellationToken);
            }

            public Task<MailDetailResponse> GetMailDetailAsync(
                long mailId,
                CancellationToken cancellationToken)
            {
                if (DetailHandler == null)
                {
                    throw new InvalidOperationException(
                        "Mail detail handler is not configured.");
                }

                return DetailHandler(mailId, cancellationToken);
            }
        }
    }
}
