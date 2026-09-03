using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Models;
using BlueServer.Client.Network;

namespace BlueServer.Client.Mail
{
    public enum MailInboxStatus
    {
        Idle,
        Loading,
        Loaded,
        Failed,
        Disconnected
    }

    public sealed class MailInboxState
    {
        private readonly IMailQueryClient _client;
        private readonly IMailCommandClient _commandClient;
        private readonly List<MailListItem> _items =
            new List<MailListItem>();

        public MailInboxState(IMailQueryClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            _client = client;
            _commandClient = client as IMailCommandClient;
            Items = _items.AsReadOnly();
            Status = MailInboxStatus.Idle;
        }

        public event Action Changed;

        public IReadOnlyList<MailListItem> Items { get; private set; }
        public MailDetail SelectedMail { get; private set; }
        public MailListCursor NextCursor { get; private set; }
        public MailInboxStatus Status { get; private set; }
        public string ErrorMessage { get; private set; }
        public MailReadResponse LastReadResult { get; private set; }
        public MailClaimResponse LastClaimResult { get; private set; }
        public MailClaimAllResponse LastClaimAllResult { get; private set; }
        public int CurrentGold { get; private set; }
        public int CurrentGem { get; private set; }
        public bool HasCurrentBalance { get; private set; }
        public bool RequiresRefresh { get; private set; }

        public bool CanExecuteCommands
        {
            get { return _commandClient != null; }
        }

        public bool IsBusy
        {
            get { return Status == MailInboxStatus.Loading; }
        }

        public bool IsEmpty
        {
            get
            {
                return Status == MailInboxStatus.Loaded &&
                    _items.Count == 0;
            }
        }

        public bool HasMore
        {
            get { return NextCursor != null; }
        }

        public async Task<bool> LoadFirstPageAsync(
            int pageSize,
            CancellationToken cancellationToken)
        {
            if (!TryBeginRequest())
            {
                return false;
            }

            try
            {
                var response = await _client.GetMailListAsync(
                    pageSize,
                    null,
                    cancellationToken);

                if (!response.Success)
                {
                    SetServerFailure(response.Message);
                    return false;
                }

                _items.Clear();
                _items.AddRange(response.Items);
                NextCursor = response.NextCursor;
                SelectedMail = null;
                RequiresRefresh = false;
                CompleteRequest();
                return true;
            }
            catch (OperationCanceledException)
            {
                CancelRequest();
                throw;
            }
            catch (Exception exception)
            {
                SetExceptionFailure(exception);
                throw;
            }
        }

        public async Task<bool> MarkAsReadAsync(
            long mailId,
            CancellationToken cancellationToken)
        {
            ValidateMailId(mailId);
            var commandClient = GetCommandClient();

            if (!TryBeginRequest())
            {
                return false;
            }

            try
            {
                var response = await commandClient.MarkMailAsReadAsync(
                    mailId,
                    cancellationToken);

                LastReadResult = response;

                if (!response.Success || !response.ReadAt.HasValue)
                {
                    if (response.Status == MailReadStatus.ConcurrencyConflict)
                    {
                        RequiresRefresh = true;
                    }

                    SetServerFailure(response.Message);
                    return false;
                }

                ApplyReadState(mailId, response.ReadAt.Value);
                CompleteRequest();
                return true;
            }
            catch (OperationCanceledException)
            {
                MarkMutationUncertain();
                CancelRequest();
                throw;
            }
            catch (Exception exception)
            {
                MarkMutationUncertain();
                SetExceptionFailure(exception);
                throw;
            }
        }

        public async Task<bool> ClaimAsync(
            long mailId,
            CancellationToken cancellationToken)
        {
            ValidateMailId(mailId);
            var commandClient = GetCommandClient();

            if (!TryBeginRequest())
            {
                return false;
            }

            try
            {
                var response = await commandClient.ClaimMailAsync(
                    mailId,
                    cancellationToken);

                LastClaimResult = response;

                if (!response.Success || !response.ClaimedAt.HasValue)
                {
                    if (response.Status == MailClaimStatus.ConcurrencyConflict ||
                        response.Status == MailClaimStatus.IdempotencyConflict)
                    {
                        RequiresRefresh = true;
                    }

                    SetServerFailure(response.Message);
                    return false;
                }

                ApplyClaimState(mailId, response.ClaimedAt.Value);
                SetCurrentBalance(
                    response.CurrentGold,
                    response.CurrentGem);
                CompleteRequest();
                return true;
            }
            catch (OperationCanceledException)
            {
                MarkMutationUncertain();
                CancelRequest();
                throw;
            }
            catch (Exception exception)
            {
                MarkMutationUncertain();
                SetExceptionFailure(exception);
                throw;
            }
        }

        public async Task<bool> ClaimAllAsync(
            CancellationToken cancellationToken)
        {
            var commandClient = GetCommandClient();

            if (!TryBeginRequest())
            {
                return false;
            }

            try
            {
                var response = await commandClient.ClaimAllMailAsync(
                    cancellationToken);

                LastClaimAllResult = response;

                if (!response.Success)
                {
                    if (response.Status ==
                            MailClaimAllStatus.ConcurrencyConflict ||
                        response.Status ==
                            MailClaimAllStatus.IdempotencyConflict)
                    {
                        RequiresRefresh = true;
                    }

                    SetServerFailure(response.Message);
                    return false;
                }

                SetCurrentBalance(
                    response.CurrentGold,
                    response.CurrentGem);

                // 응답에 처리 대상 Mail ID가 없으므로 결과와 무관하게 목록 재조회
                RequiresRefresh = true;
                CompleteRequest();
                return true;
            }
            catch (OperationCanceledException)
            {
                MarkMutationUncertain();
                CancelRequest();
                throw;
            }
            catch (Exception exception)
            {
                MarkMutationUncertain();
                SetExceptionFailure(exception);
                throw;
            }
        }

        public async Task<bool> LoadNextPageAsync(
            int pageSize,
            CancellationToken cancellationToken)
        {
            if (NextCursor == null || !TryBeginRequest())
            {
                return false;
            }

            var cursor = NextCursor;

            try
            {
                var response = await _client.GetMailListAsync(
                    pageSize,
                    cursor,
                    cancellationToken);

                if (!response.Success)
                {
                    SetServerFailure(response.Message);
                    return false;
                }

                AppendUnique(response.Items);
                NextCursor = response.NextCursor;
                CompleteRequest();
                return true;
            }
            catch (OperationCanceledException)
            {
                CancelRequest();
                throw;
            }
            catch (Exception exception)
            {
                SetExceptionFailure(exception);
                throw;
            }
        }

        public async Task<bool> LoadDetailAsync(
            long mailId,
            CancellationToken cancellationToken)
        {
            if (mailId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mailId));
            }

            if (!TryBeginRequest())
            {
                return false;
            }

            SelectedMail = null;
            NotifyChanged();

            try
            {
                var response = await _client.GetMailDetailAsync(
                    mailId,
                    cancellationToken);

                if (!response.Success || response.Mail == null)
                {
                    SetServerFailure(response.Message);
                    return false;
                }

                SelectedMail = response.Mail;
                CompleteRequest();
                return true;
            }
            catch (OperationCanceledException)
            {
                CancelRequest();
                throw;
            }
            catch (Exception exception)
            {
                SetExceptionFailure(exception);
                throw;
            }
        }

        private bool TryBeginRequest()
        {
            if (IsBusy)
            {
                return false;
            }

            Status = MailInboxStatus.Loading;
            ErrorMessage = null;
            NotifyChanged();
            return true;
        }

        private void AppendUnique(IReadOnlyList<MailListItem> items)
        {
            var existingIds = new HashSet<long>();

            foreach (var existingItem in _items)
            {
                existingIds.Add(existingItem.Id);
            }

            foreach (var item in items)
            {
                if (existingIds.Add(item.Id))
                {
                    _items.Add(item);
                }
            }
        }

        private void ApplyReadState(long mailId, DateTime readAt)
        {
            for (var index = 0; index < _items.Count; index++)
            {
                var item = _items[index];

                if (item.Id == mailId)
                {
                    _items[index] = CopyListItem(
                        item,
                        true,
                        item.IsClaimed,
                        item.CanClaim);
                    break;
                }
            }

            if (SelectedMail != null && SelectedMail.Id == mailId)
            {
                SelectedMail = CopyDetail(
                    SelectedMail,
                    readAt,
                    SelectedMail.ClaimedAt,
                    SelectedMail.CanClaim);
            }
        }

        private void ApplyClaimState(long mailId, DateTime claimedAt)
        {
            for (var index = 0; index < _items.Count; index++)
            {
                var item = _items[index];

                if (item.Id == mailId)
                {
                    _items[index] = CopyListItem(
                        item,
                        true,
                        true,
                        false);
                    break;
                }
            }

            if (SelectedMail != null && SelectedMail.Id == mailId)
            {
                SelectedMail = CopyDetail(
                    SelectedMail,
                    SelectedMail.ReadAt ?? claimedAt,
                    claimedAt,
                    false);
            }
        }

        private static MailListItem CopyListItem(
            MailListItem item,
            bool isRead,
            bool isClaimed,
            bool canClaim)
        {
            return new MailListItem(
                item.Id,
                item.Title,
                item.SentAt,
                item.ExpiresAt,
                isRead,
                isClaimed,
                item.IsExpired,
                canClaim,
                item.AttachmentCount);
        }

        private static MailDetail CopyDetail(
            MailDetail mail,
            DateTime? readAt,
            DateTime? claimedAt,
            bool canClaim)
        {
            return new MailDetail(
                mail.Id,
                mail.Title,
                mail.Body,
                mail.SentAt,
                mail.ExpiresAt,
                readAt,
                claimedAt,
                mail.IsExpired,
                canClaim,
                mail.Attachments);
        }

        private void SetCurrentBalance(int gold, int gem)
        {
            CurrentGold = gold;
            CurrentGem = gem;
            HasCurrentBalance = true;
        }

        private void MarkMutationUncertain()
        {
            RequiresRefresh = true;
        }

        private IMailCommandClient GetCommandClient()
        {
            if (_commandClient == null)
            {
                throw new InvalidOperationException(
                    "The configured Mail client does not support commands.");
            }

            return _commandClient;
        }

        private static void ValidateMailId(long mailId)
        {
            if (mailId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mailId));
            }
        }

        private void CompleteRequest()
        {
            Status = MailInboxStatus.Loaded;
            ErrorMessage = null;
            NotifyChanged();
        }

        private void SetServerFailure(string message)
        {
            Status = MailInboxStatus.Failed;
            ErrorMessage = string.IsNullOrWhiteSpace(message)
                ? "Mail request failed."
                : message;
            NotifyChanged();
        }

        private void SetExceptionFailure(Exception exception)
        {
            Status = IsConnectionFailure(exception)
                ? MailInboxStatus.Disconnected
                : MailInboxStatus.Failed;
            ErrorMessage = exception.Message;
            NotifyChanged();
        }

        private void CancelRequest()
        {
            Status = _items.Count == 0
                ? MailInboxStatus.Idle
                : MailInboxStatus.Loaded;
            ErrorMessage = null;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            var handler = Changed;

            if (handler != null)
            {
                handler();
            }
        }

        private static bool IsConnectionFailure(Exception exception)
        {
            return exception is IOException ||
                exception is SocketException ||
                exception is ObjectDisposedException;
        }
    }
}
