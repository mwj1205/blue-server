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
            if (!_initialized)
            {
                return;
            }

            var width = Mathf.Min(WindowWidth, Screen.width - 20f);
            var area = new Rect(10f, 10f, width, Screen.height - 20f);

            GUILayout.BeginArea(area, GUI.skin.window);
            DrawHeader();
            DrawStatus();

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

            if (_state.HasCurrentBalance)
            {
                GUILayout.Label(string.Format(
                    "Gold: {0:N0}    Gem: {1:N0}",
                    _state.CurrentGold,
                    _state.CurrentGem));
            }
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
                    260f,
                    Screen.height * 0.35f));

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
