using System;
using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Mail;
using BlueServer.Client.Models;
using BlueServer.Client.Network;
using NUnit.Framework;

namespace BlueServer.Client.Tests
{
    public sealed class InventoryInboxStateTests
    {
        [Test]
        public void Refresh_ReplacesWholeSnapshotAndTreatsMissingItemAsZeroOnlyWhenFresh()
        {
            Refresh_ReplacesWholeSnapshotAndTreatsMissingItemAsZeroOnlyWhenFreshAsync().GetAwaiter().GetResult();
        }

        private static async Task Refresh_ReplacesWholeSnapshotAndTreatsMissingItemAsZeroOnlyWhenFreshAsync()
        {
            var client = new FakeClient();
            var state = new MailInboxState(client);
            int quantity;
            Assert.That(state.TryGetInventoryQuantity(3101, out quantity), Is.False);
            Assert.That(await state.RefreshInventoryAsync(CancellationToken.None), Is.True);
            Assert.That(state.TryGetInventoryQuantity(3101, out quantity), Is.True);
            Assert.That(quantity, Is.EqualTo(5));
            client.Inventory = () => Task.FromResult(Response());
            Assert.That(await state.RefreshInventoryAsync(CancellationToken.None), Is.True);
            Assert.That(state.InventoryItems, Is.Empty);
            Assert.That(state.KnownQuantities, Is.Empty);
            Assert.That(state.TryGetInventoryQuantity(3101, out quantity), Is.True);
            Assert.That(quantity, Is.Zero);
        }

        [Test]
        public void Refresh_DoesNotPublishPendingSnapshotOrAllowOverlappingRequest()
        {
            Refresh_DoesNotPublishPendingSnapshotOrAllowOverlappingRequestAsync().GetAwaiter().GetResult();
        }

        private static async Task Refresh_DoesNotPublishPendingSnapshotOrAllowOverlappingRequestAsync()
        {
            var client = new FakeClient();
            var state = new MailInboxState(client);
            await state.RefreshInventoryAsync(CancellationToken.None);
            var pending = new TaskCompletionSource<InventoryListResponse>();
            client.Inventory = () => pending.Task;
            var refresh = state.RefreshInventoryAsync(CancellationToken.None);
            Assert.That(state.IsInventoryLoading, Is.True);
            Assert.That(state.IsInventoryFresh, Is.False);
            Assert.That(state.InventoryItems.Count, Is.EqualTo(1));
            Assert.That(await state.RefreshInventoryAsync(CancellationToken.None), Is.False);
            pending.SetResult(Response());
            Assert.That(await refresh, Is.True);
            Assert.That(state.IsBusy, Is.False);
            Assert.That(state.IsInventoryLoading, Is.False);
        }

        [Test]
        public void Refresh_FailurePreservesStaleSnapshotAndRetryOnlyQueriesInventory()
        {
            Refresh_FailurePreservesStaleSnapshotAndRetryOnlyQueriesInventoryAsync().GetAwaiter().GetResult();
        }

        private static async Task Refresh_FailurePreservesStaleSnapshotAndRetryOnlyQueriesInventoryAsync()
        {
            var client = new FakeClient();
            var state = new MailInboxState(client);
            await state.RefreshInventoryAsync(CancellationToken.None);
            client.Inventory = () => Task.FromResult(new InventoryListResponse(
                Guid.NewGuid(), InventoryListStatus.InventoryTooLarge, new InventoryListItem[0]));
            Assert.That(await state.RefreshInventoryAsync(CancellationToken.None), Is.False);
            Assert.That(state.InventoryItems.Count, Is.EqualTo(1));
            Assert.That(state.IsInventoryFresh, Is.False);
            Assert.That(state.InventoryError, Does.Contain("InventoryTooLarge"));
            client.Inventory = () => Task.FromResult(Response());
            Assert.That(await state.RefreshInventoryAsync(CancellationToken.None), Is.True);
            Assert.That(state.InventoryError, Is.Null);
            Assert.That(client.Mutations, Is.Zero);
        }

        [Test]
        public void Refresh_CancellationReleasesBusyStateAndDoesNotMarkOldSnapshotFresh()
        {
            Refresh_CancellationReleasesBusyStateAndDoesNotMarkOldSnapshotFreshAsync().GetAwaiter().GetResult();
        }

        private static async Task Refresh_CancellationReleasesBusyStateAndDoesNotMarkOldSnapshotFreshAsync()
        {
            var client = new FakeClient();
            var state = new MailInboxState(client);
            await state.RefreshInventoryAsync(CancellationToken.None);
            client.Inventory = () => Task.FromCanceled<InventoryListResponse>(new CancellationToken(true));
            try
            {
                await state.RefreshInventoryAsync(CancellationToken.None);
                Assert.Fail("Cancelled inventory query must throw.");
            }
            catch (OperationCanceledException)
            {
            }
            Assert.That(state.IsInventoryFresh, Is.False);
            Assert.That(state.IsInventoryLoading, Is.False);
            Assert.That(state.IsBusy, Is.False);
        }

        [TestCase("claim")]
        [TestCase("all")]
        [TestCase("use")]
        public void Mutation_RefreshesAuthoritativeInventoryInsteadOfUsingOldMutationSnapshot(string operation)
        {
            Mutation_RefreshesAuthoritativeInventoryInsteadOfUsingOldMutationSnapshotAsync(operation).GetAwaiter().GetResult();
        }

        private static async Task Mutation_RefreshesAuthoritativeInventoryInsteadOfUsingOldMutationSnapshotAsync(string operation)
        {
            var client = new FakeClient();
            var state = new MailInboxState(client);
            await state.RefreshInventoryAsync(CancellationToken.None);
            client.Inventory = () => Task.FromResult(Response(new InventoryListItem(
                3101, 12, InventoryItemType.Consumable, false)));
            bool result;
            if (operation == "claim") result = await state.ClaimAsync(1, CancellationToken.None);
            else if (operation == "all") result = await state.ClaimAllAsync(CancellationToken.None);
            else result = await state.UseBoxAsync(3101, 1, false, CancellationToken.None);
            Assert.That(result, Is.True);
            Assert.That(client.Queries, Is.EqualTo(2));
            Assert.That(client.Mutations, Is.EqualTo(1));
            Assert.That(state.IsInventoryFresh, Is.True);
            Assert.That(state.KnownQuantities[3101], Is.EqualTo(12));
            Assert.That(state.InventoryItems[0].IsActive, Is.False);
        }

        [Test]
        public void Use_RefreshFailureDoesNotResendConfirmedMutationOnQueryRetry()
        {
            Use_RefreshFailureDoesNotResendConfirmedMutationOnQueryRetryAsync().GetAwaiter().GetResult();
        }

        private static async Task Use_RefreshFailureDoesNotResendConfirmedMutationOnQueryRetryAsync()
        {
            var client = new FakeClient();
            var state = new MailInboxState(client);
            client.Inventory = () => Task.FromResult(new InventoryListResponse(
                Guid.NewGuid(), InventoryListStatus.PlayerNotFound, new InventoryListItem[0]));
            Assert.That(await state.UseBoxAsync(3101, 1, false, CancellationToken.None), Is.False);
            Assert.That(state.ErrorMessage, Does.Contain("Box use confirmed"));
            client.Inventory = () => Task.FromResult(Response());
            Assert.That(await state.RefreshInventoryAsync(CancellationToken.None), Is.True);
            Assert.That(client.Mutations, Is.EqualTo(1));
        }

        private static InventoryListResponse Response(params InventoryListItem[] items)
        {
            return new InventoryListResponse(Guid.NewGuid(), InventoryListStatus.Success, Array.AsReadOnly(items));
        }

        private sealed class FakeClient : IMailQueryClient, IInventoryQueryClient, IItemUseClient
        {
            public int Queries;
            public int Mutations;
            public Func<Task<InventoryListResponse>> Inventory = () => Task.FromResult(Response(
                new InventoryListItem(3101, 5, InventoryItemType.Consumable, true)));

            public Task<InventoryListResponse> GetInventoryAsync(CancellationToken ct)
            {
                Queries++;
                return Inventory();
            }

            public Task<MailListResponse> GetMailListAsync(int pageSize, MailListCursor cursor, CancellationToken ct)
            {
                return Task.FromResult(new MailListResponse(true, "ok", new MailListItem[0], null));
            }

            public Task<MailDetailResponse> GetMailDetailAsync(long mailId, CancellationToken ct)
            {
                return Task.FromResult(new MailDetailResponse(true, "ok", new MailDetail(
                    mailId, "test", "test", DateTime.UtcNow, null, null, null, false, true, new MailAttachment[0])));
            }

            public Task<ItemMailClaimResponse> ClaimMailItemsAsync(long mailId, Guid requestId, CancellationToken ct)
            {
                Mutations++;
                return Task.FromResult(new ItemMailClaimResponse
                {
                    RequestId = requestId, Success = true, Message = "ok", CurrentGold = 1000, CurrentGem = 500,
                    Items = new[] { new ClaimedInventoryItem { ItemTemplateId = 3101, QuantityAfter = 8 } }
                });
            }

            public Task<CurrencyBoxUseResponse> UseCurrencyBoxAsync(int itemId, int quantity, Guid requestId, CancellationToken ct)
            {
                Mutations++;
                return Task.FromResult(new CurrencyBoxUseResponse
                {
                    RequestId = requestId, Status = 0, ItemTemplateId = itemId, UsedQuantity = quantity,
                    QuantityAfter = 4, CurrentGold = 1100, CurrentGem = 500
                });
            }

            public Task<PlayerProfileResponse> GetPlayerProfileAsync(CancellationToken ct)
            {
                return Task.FromResult(new PlayerProfileResponse(true, "ok", 1, "test", 1100, 500, 0, 0, 0, 0));
            }

            public Task<ItemMailClaimAllResponse> ClaimAllMailItemsAsync(Guid requestId, CancellationToken ct)
            {
                Mutations++;
                return Task.FromResult(new ItemMailClaimAllResponse
                {
                    RequestId = requestId, Status = 5,
                    Items = new[] { new GrantedInventoryItem { ItemTemplateId = 3101, GrantedQuantity = 3 } }
                });
            }
        }
    }
}
