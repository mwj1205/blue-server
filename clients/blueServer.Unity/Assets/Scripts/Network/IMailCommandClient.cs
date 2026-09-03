using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Models;

namespace BlueServer.Client.Network
{
    public interface IMailCommandClient
    {
        Task<MailReadResponse> MarkMailAsReadAsync(
            long mailId,
            CancellationToken cancellationToken);

        Task<MailClaimResponse> ClaimMailAsync(
            long mailId,
            CancellationToken cancellationToken);

        Task<MailClaimAllResponse> ClaimAllMailAsync(
            CancellationToken cancellationToken);
    }
}
