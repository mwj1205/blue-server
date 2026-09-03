using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Models;

namespace BlueServer.Client.Network
{
    public interface IMailQueryClient
    {
        Task<MailListResponse> GetMailListAsync(
            int pageSize,
            MailListCursor cursor,
            CancellationToken cancellationToken);

        Task<MailDetailResponse> GetMailDetailAsync(
            long mailId,
            CancellationToken cancellationToken);
    }
}
