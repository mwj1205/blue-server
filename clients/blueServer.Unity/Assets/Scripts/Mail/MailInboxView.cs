using System;
using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Models;
using UnityEngine;

namespace BlueServer.Client.Mail
{
    public sealed class MailInboxView : MonoBehaviour
    {
        private const int PageSize = 20;
        private const float WindowWidth = 720f;

        private MailInboxState _state;
        private CancellationToken _cancellationToken;
        private Vector2 _listScrollPosition;
        private Vector2 _detailScrollPosition;
        private bool _initialized;
        private int _boxIndex;
        private string _boxQuantity = "20";
        private int _tab;
        private Vector2 _resultScrollPosition;
        private Vector2 _resourceScrollPosition;
        private float _layoutHeight;
        private GUIStyle _resourceValueStyle;
        private int _inventoryCategory;
        private Vector2 _inventoryScrollPosition;

        public void Initialize(
            MailInboxState state,
            CancellationToken cancellationToken)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (_initialized)
            {
                throw new InvalidOperationException(
                    "Mail inbox view is already initialized.");
            }

            _state = state;
            _cancellationToken = cancellationToken;
            _initialized = true;
        }

        private void OnGUI()
        {
            if (!_initialized || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            // 작은 Game View에서도 두 패널이 겹치지 않도록 최소 논리 폭에 맞춰 축소한다.
            var scale = Mathf.Min(1f, Screen.width / 720f);
            var canvasWidth = Screen.width / scale;
            _layoutHeight = Screen.height / scale;
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                var resources = new Rect(canvasWidth - 230f, 10f, 220f, _layoutHeight - 20f);
                DrawResources(resources);
                var width = Mathf.Min(WindowWidth, canvasWidth - 250f);
                DrawMainArea(new Rect(10f, 10f, width, _layoutHeight - 20f));
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        private void DrawMainArea(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.window);
            DrawHeader();
            DrawStatus();
            _tab = GUILayout.Toolbar(_tab, new[] { "Mail", "Inventory / Box Use" });
            if (!string.IsNullOrEmpty(_state.ItemMessage))
            {
                _resultScrollPosition = GUILayout.BeginScrollView(_resultScrollPosition,
                    GUI.skin.box, GUILayout.Height(Mathf.Min(110f, _layoutHeight * 0.2f)));
                GUILayout.Label(_state.ItemMessage);
                GUILayout.EndScrollView();
            }
            if (_tab == 1)
            {
                DrawBoxUse();
                DrawInventory();
                GUILayout.EndArea();
                return;
            }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();

            var commandsEnabled = GUI.enabled;
            GUI.enabled = commandsEnabled && !_state.IsBusy;

            if (GUILayout.Button("Refresh", GUILayout.Width(100f)))
            {
                LoadFirstPage();
            }

            GUI.enabled = commandsEnabled &&
                !_state.IsBusy &&
                _state.CanExecuteCommands;

            if (GUILayout.Button("Claim All", GUILayout.Width(100f)))
            {
                ClaimAll();
            }

            GUI.enabled = commandsEnabled && !_state.IsBusy && _state.CanUseItems && _state.CanRetryClaimAll;
            if (GUILayout.Button("Retry Claim All", GUILayout.Width(130f)))
            {
                RetryClaimAll();
            }

            GUI.enabled = commandsEnabled;
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            DrawMailList();
            GUILayout.Space(8f);
            DrawSelectedMail();
            GUILayout.EndArea();
        }

        private void DrawHeader()
        {
            GUILayout.Label("Blue Server Mail", GUI.skin.box);
        }

        private void DrawResources(Rect area)
        {
            if (_resourceValueStyle == null)
            {
                _resourceValueStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                    wordWrap = true
                };
            }
            GUILayout.BeginArea(area, GUI.skin.window);
            GUILayout.Label("Player Resources", GUI.skin.box);
            _resourceScrollPosition = GUILayout.BeginScrollView(_resourceScrollPosition);
            GUILayout.Label("Gold");
            GUILayout.Label(_state.HasCurrentBalance ? _state.CurrentGold.ToString("N0") : "Unknown", _resourceValueStyle);
            GUILayout.Label("Gem");
            GUILayout.Label(_state.HasCurrentBalance ? _state.CurrentGem.ToString("N0") : "Unknown", _resourceValueStyle);
            GUILayout.Space(10f);
            GUILayout.Label("Boxes / server inventory");
            var names = new[] { "Gold 100", "Gem 100", "Gold 20-200", "Gem 20-200" };
            for (var i = 0; i < names.Length; i++)
            {
                var itemId = 3101 + i;
                int quantity;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(itemId + " / " + names[i]);
                GUILayout.Label(_state.TryGetInventoryQuantity(itemId, out quantity)
                    ? quantity.ToString("N0") : "Unknown", _resourceValueStyle);
                GUILayout.EndVertical();
            }
            GUILayout.Label(_state.IsInventoryLoading ? "Loading inventory..." :
                _state.IsInventoryFresh ? "Server snapshot loaded." : "Inventory needs refresh.");
            if (!string.IsNullOrEmpty(_state.InventoryError)) GUILayout.Label(_state.InventoryError);
            GUILayout.Label("Unknown is not zero. Successful claims/use refresh the full inventory.");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawStatus()
        {
            switch (_state.Status)
            {
                case MailInboxStatus.Idle:
                    GUILayout.Label("Mail is not loaded.");
                    break;
                case MailInboxStatus.Loading:
                    GUILayout.Label("Loading...");
                    break;
                case MailInboxStatus.Loaded:
                    if (_state.IsEmpty)
                    {
                        GUILayout.Label("No Mail.");
                    }
                    break;
                case MailInboxStatus.Failed:
                    GUILayout.Label("Request failed: " + _state.ErrorMessage);
                    break;
                case MailInboxStatus.Disconnected:
                    GUILayout.Label(
                        "Connection lost: " + _state.ErrorMessage);
                    break;
                default:
                    GUILayout.Label("Unknown Mail state.");
                    break;
            }

            if (_state.RequiresRefresh)
            {
                GUILayout.Label(
                    "Server state may have changed. Refresh the Mail list.");
            }
        }

        private void DrawMailList()
        {
            GUILayout.Label("Mail List");
            var listHeight = _state.SelectedMail == null
                ? GUILayout.ExpandHeight(true)
                : GUILayout.Height(Mathf.Min(
                    160f,
                    _layoutHeight * 0.22f));

            _listScrollPosition = GUILayout.BeginScrollView(
                _listScrollPosition,
                GUI.skin.box,
                listHeight);

            foreach (var item in _state.Items)
            {
                DrawMailListItem(item);
            }

            GUILayout.EndScrollView();

            if (_state.HasMore)
            {
                var previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && !_state.IsBusy;

                if (GUILayout.Button("Load More", GUILayout.Width(120f)))
                {
                    LoadNextPage();
                }

                GUI.enabled = previousEnabled;
            }
        }

        private void DrawMailListItem(MailListItem item)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(item.Title, GUILayout.ExpandWidth(true));
            GUILayout.Label(
                FormatMailState(item),
                GUILayout.Width(150f));

            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !_state.IsBusy;

            if (GUILayout.Button("Details", GUILayout.Width(80f)))
            {
                LoadDetail(item.Id);
            }

            GUI.enabled = previousEnabled;
            GUILayout.EndHorizontal();
            GUILayout.Label(string.Format(
                "Sent: {0:u}    Attachments: {1}",
                item.SentAt,
                item.AttachmentCount));

            if (item.ExpiresAt.HasValue)
            {
                GUILayout.Label("Expires: " + item.ExpiresAt.Value.ToString("u"));
            }

            GUILayout.EndVertical();
        }

        private void DrawSelectedMail()
        {
            var mail = _state.SelectedMail;

            if (mail == null)
            {
                return;
            }

            GUILayout.Label("Mail Detail");
            _detailScrollPosition = GUILayout.BeginScrollView(
                _detailScrollPosition,
                GUI.skin.box,
                GUILayout.ExpandHeight(true));
            GUILayout.Label(mail.Title);
            GUILayout.Label(mail.Body);
            GUILayout.Label("Sent: " + mail.SentAt.ToString("u"));
            GUILayout.Label("Expires: " + (mail.ExpiresAt.HasValue ? mail.ExpiresAt.Value.ToString("u") : "No expiry"));

            if (mail.ReadAt.HasValue)
            {
                GUILayout.Label("Read: " + mail.ReadAt.Value.ToString("u"));
            }

            if (mail.ClaimedAt.HasValue)
            {
                GUILayout.Label(
                    "Claimed: " + mail.ClaimedAt.Value.ToString("u"));
            }

            GUILayout.Space(4f);
            GUILayout.Label("Attachments");

            foreach (var attachment in mail.Attachments)
            {
                GUILayout.Label(string.Format(
                    "- {0}: {1:N0}",
                    FormatRewardType(attachment.RewardType),
                    attachment.Amount));
            }
            foreach (var item in mail.ItemAttachments)
            {
                GUILayout.Label(string.Format("- Item {0}: total {1:N0}, claimed {2:N0}, remaining {3:N0}",
                    item.ItemTemplateId, item.Quantity, item.ClaimedQuantity, item.RemainingQuantity));
            }

            GUILayout.Space(6f);
            DrawDetailActions(mail);
            GUILayout.EndScrollView();
        }

        private void DrawDetailActions(MailDetail mail)
        {
            GUILayout.BeginHorizontal();

            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled &&
                !_state.IsBusy &&
                _state.CanExecuteCommands;

            if (!mail.ReadAt.HasValue &&
                GUILayout.Button("Mark as Read", GUILayout.Width(120f)))
            {
                MarkAsRead(mail.Id);
            }

            GUI.enabled = previousEnabled &&
                !_state.IsBusy &&
                _state.CanExecuteCommands &&
                mail.CanClaim;

            if (GUILayout.Button("Claim", GUILayout.Width(100f)))
            {
                Claim(mail.Id);
            }

            GUI.enabled = previousEnabled;
            GUILayout.EndHorizontal();
        }

        private async void LoadFirstPage()
        {
            await ExecuteAsync(() => _state.LoadFirstPageAsync(
                PageSize,
                _cancellationToken));
        }

        private void DrawBoxUse()
        {
            if (!_state.CanUseItems) return;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Currency boxes (server test)");
            _boxIndex = GUILayout.SelectionGrid(_boxIndex,
                new[] { "3101 Gold100", "3102 Gem100", "3103 Gold20-200", "3104 Gem20-200" }, 2);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Use quantity", GUILayout.Width(90f));
            _boxQuantity = GUILayout.TextField(_boxQuantity, 6, GUILayout.Width(80f));
            int quantity;
            var valid = int.TryParse(_boxQuantity, out quantity) && quantity >= 1 && quantity <= 999999;
            var previous = GUI.enabled;
            GUI.enabled = previous && !_state.IsBusy && valid && _state.IsInventoryFresh;
            if (GUILayout.Button("Use (new request)")) UseBox(3101 + _boxIndex, quantity, false);
            GUI.enabled = previous && !_state.IsBusy && _state.CanRetryUse;
            if (GUILayout.Button("Retry last request")) UseBox(0, 0, true);
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawInventory()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Inventory / " + _state.InventoryItems.Count + " types");
            var previous = GUI.enabled;
            GUI.enabled = previous && !_state.IsBusy && _state.CanQueryInventory;
            if (GUILayout.Button("Refresh inventory", GUILayout.Width(150f))) RefreshInventory();
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            if (_state.IsInventoryLoading) GUILayout.Label("Loading full inventory...");
            else if (!_state.IsInventoryFresh) GUILayout.Label("Not current. Refresh inventory; reconnect if the connection was lost.");
            if (!string.IsNullOrEmpty(_state.InventoryError)) GUILayout.Label(_state.InventoryError);

            // 카테고리는 이미 수신한 전체 목록만 필터링한다. 전환 시 서버 요청은 없다.
            _inventoryCategory = GUILayout.Toolbar(_inventoryCategory,
                new[] { "All", "Materials", "Shards", "Consumables", "Event" });
            _inventoryScrollPosition = GUILayout.BeginScrollView(
                _inventoryScrollPosition, GUI.skin.box, GUILayout.ExpandHeight(true));
            var visibleCount = 0;
            foreach (var item in _state.InventoryItems)
            {
                if (_inventoryCategory != 0 && (int)item.Type != _inventoryCategory) continue;
                visibleCount++;
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(item.ItemTemplateId + " / " + item.Type);
                GUILayout.Label(item.Quantity.ToString("N0") + (_state.IsInventoryFresh ? "" : " (stale)"));
                if (!item.IsActive) GUILayout.Label("Inactive");
                GUILayout.EndHorizontal();
            }
            if (visibleCount == 0) GUILayout.Label(_state.IsInventoryFresh ? "No items in this category." : "No inventory snapshot.");
            GUILayout.EndScrollView();
        }

        private async void RefreshInventory()
        {
            await ExecuteAsync(() => _state.RefreshInventoryAsync(_cancellationToken));
        }

        private async void UseBox(int itemId, int quantity, bool retry)
        {
            await ExecuteAsync(() => _state.UseBoxAsync(itemId, quantity, retry, _cancellationToken));
        }

        private async void LoadNextPage()
        {
            await ExecuteAsync(() => _state.LoadNextPageAsync(
                PageSize,
                _cancellationToken));
        }

        private async void LoadDetail(long mailId)
        {
            await ExecuteAsync(() => _state.LoadDetailAsync(
                mailId,
                _cancellationToken));
        }

        private async void MarkAsRead(long mailId)
        {
            await ExecuteAsync(() => _state.MarkAsReadAsync(
                mailId,
                _cancellationToken));
        }

        private async void Claim(long mailId)
        {
            await ExecuteAsync(() => _state.ClaimAsync(
                mailId,
                _cancellationToken));
        }

        private async void ClaimAll()
        {
            var completed = await ExecuteAsync(() =>
                _state.ClaimAllAsync(_cancellationToken));

            if (completed && _state.RequiresRefresh)
            {
                await ExecuteAsync(() => _state.LoadFirstPageAsync(
                    PageSize,
                    _cancellationToken));
            }
        }

        private async void RetryClaimAll()
        {
            await ExecuteAsync(() => _state.ClaimAllItemsAsync(true, _cancellationToken));
        }

        private static string FormatMailState(MailListItem item)
        {
            if (item.IsExpired)
            {
                return "Expired";
            }

            if (item.IsClaimed)
            {
                return "Claimed";
            }

            if (!item.IsRead)
            {
                return "Unread";
            }

            return item.CanClaim ? "Claimable" : "Read";
        }

        private static string FormatRewardType(int rewardType)
        {
            switch (rewardType)
            {
                case 1:
                    return "Gold";
                case 2:
                    return "Gem";
                default:
                    return "Reward " + rewardType;
            }
        }

        private static async Task<bool> ExecuteAsync(
            Func<Task<bool>> operation)
        {
            try
            {
                return await operation();
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }
    }
}
