using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Mail;
using BlueServer.Client.Network;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace BlueServer.Client.Tests
{
    public sealed class ItemUseLiveServerTests
    {
        [UnityTest]
        public IEnumerator PartialMailClaim_UseBoxAndClaimRemainderAgainstLiveServer()
        {
            var token = Environment.GetEnvironmentVariable("BLUE_SERVER_UNITY_LIVE_TEST_TOKEN");
            var port = Environment.GetEnvironmentVariable("BLUE_SERVER_UNITY_LIVE_TEST_PORT");
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(port))
                Assert.Ignore("Dedicated live server fixture/token is not configured.");
            var task = RunAsync(token, int.Parse(port));
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception.InnerException;
            if (task.IsCanceled) Assert.Fail("Live server test timed out.");
        }

        private static async Task RunAsync(string token, int port)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (var client = new GameTcpClient("127.0.0.1", port))
            {
                var ct = timeout.Token;
                await client.ConnectAsync(ct);
                Assert.That((await client.LoginAsync(token, ct)).Success, Is.True);
                var state = new MailInboxState(client);
                Assert.That(await state.LoadFirstPageAsync(20, ct), Is.True);
                Assert.That(await state.RefreshInventoryAsync(ct), Is.True);
                Assert.That(state.IsInventoryFresh, Is.True);
                Assert.That(state.KnownQuantities[3101], Is.EqualTo(999990));
                Assert.That(state.InventoryItems.Select(item => item.Type).Distinct().Count(), Is.EqualTo(4));
                var mail = state.Items.Single(item => item.Title == "Unity box overflow 3101");
                Assert.That(await state.LoadDetailAsync(mail.Id, ct), Is.True);
                var expiresAt = state.SelectedMail.ExpiresAt;
                Assert.That(state.SelectedMail.ItemAttachments[0].RemainingQuantity, Is.EqualTo(20));
                Assert.That(await state.ClaimAsync(mail.Id, ct), Is.True);
                Assert.That(state.KnownQuantities[3101], Is.EqualTo(999999));
                Assert.That(state.SelectedMail.ItemAttachments[0].RemainingQuantity, Is.EqualTo(11));
                Assert.That(state.SelectedMail.ClaimedAt, Is.Null);
                Assert.That(state.SelectedMail.ExpiresAt, Is.EqualTo(expiresAt));
                Assert.That(await state.UseBoxAsync(3101, 20, false, ct), Is.True);
                Assert.That(state.CurrentGold, Is.EqualTo(3000));
                Assert.That(state.KnownQuantities[3101], Is.EqualTo(999979));
                Assert.That(await state.ClaimAsync(mail.Id, ct), Is.True);
                Assert.That(state.KnownQuantities[3101], Is.EqualTo(999990));
                Assert.That(state.SelectedMail.ClaimedAt, Is.Not.Null);
                Assert.That(state.SelectedMail.ExpiresAt, Is.EqualTo(expiresAt));
                Assert.That(await state.UseBoxAsync(0, 0, true, ct), Is.True);
                Assert.That(state.CurrentGold, Is.EqualTo(3000));
                Assert.That(state.KnownQuantities[3101], Is.EqualTo(999990));
                var gemMail = state.Items.Single(item => item.Title == "Unity box overflow 3102");
                var gemExpiry = gemMail.ExpiresAt;
                Assert.That(await state.ClaimAllAsync(ct), Is.True);
                var firstBatchId = state.LastItemClaimAllResult.RequestId;
                Assert.That(state.LastItemClaimAllResult.PartiallyClaimedMailCount, Is.EqualTo(3));
                Assert.That(state.LastItemClaimAllResult.Items.Count, Is.EqualTo(3));
                Assert.That(state.LastItemClaimAllResult.Items.All(item => item.GrantedQuantity == 9), Is.True);
                Assert.That(await state.ClaimAllItemsAsync(true, ct), Is.True);
                Assert.That(state.LastItemClaimAllResult.RequestId, Is.EqualTo(firstBatchId));
                Assert.That(state.CurrentGold, Is.EqualTo(3000));
                Assert.That(state.CurrentGem, Is.EqualTo(500));
                Assert.That(await state.ClaimAllAsync(ct), Is.True);
                Assert.That(state.LastItemClaimAllResult.NoCapacityMailCount, Is.EqualTo(3));
                Assert.That(state.LastItemClaimAllResult.Items.Count, Is.EqualTo(0));
                Assert.That(await state.UseBoxAsync(3102, 20, false, ct), Is.True);
                Assert.That(state.CurrentGem, Is.EqualTo(2500));
                Assert.That(await state.ClaimAllAsync(ct), Is.True);
                Assert.That(state.LastItemClaimAllResult.ClaimedMailCount, Is.EqualTo(1));
                Assert.That(state.LastItemClaimAllResult.NoCapacityMailCount, Is.EqualTo(2));
                Assert.That(state.LastItemClaimAllResult.Items.Single().GrantedQuantity, Is.EqualTo(11));
                Assert.That(state.IsInventoryFresh, Is.True);
                Assert.That(state.KnownQuantities[3102], Is.EqualTo(999990));
                Assert.That(await state.LoadDetailAsync(gemMail.Id, ct), Is.True);
                Assert.That(state.SelectedMail.ItemAttachments.Single().RemainingQuantity, Is.EqualTo(0));
                Assert.That(state.SelectedMail.ExpiresAt, Is.EqualTo(gemExpiry));
                Assert.That(state.SelectedMail.ClaimedAt, Is.Not.Null);
            }
        }
    }
}
