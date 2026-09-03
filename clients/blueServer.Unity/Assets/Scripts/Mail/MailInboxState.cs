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
        private readonly List<MailListItem> _items =
            new List<MailListItem>();

        public MailInboxState(IMailQueryClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            _client = client;
            Items = _items.AsReadOnly();
            Status = MailInboxStatus.Idle;
        }

        public event Action Changed;

        public IReadOnlyList<MailListItem> Items { get; private set; }
        public MailDetail SelectedMail { get; private set; }
        public MailListCursor NextCursor { get; private set; }
        public MailInboxStatus Status { get; private set; }
        public string ErrorMessage { get; private set; }

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
