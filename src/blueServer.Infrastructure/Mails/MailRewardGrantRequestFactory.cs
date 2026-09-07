using System.Buffers.Binary;
using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure.Rewards;

namespace blueServer.Infrastructure.Mails;

internal static class MailRewardGrantRequestFactory
{
    private static readonly Guid MailRewardNamespaceId = new(
        "f708b7c3-9119-4b9e-a114-a41029c2b7d7");

    public static RewardGrantRequest Create(Mail mail)
    {
        ArgumentNullException.ThrowIfNull(mail);

        var rewards = RewardBundle.Create(
            mail.Attachments.Select(attachment =>
                RewardItem.Create(
                    attachment.Type,
                    attachment.Amount)));

        return new RewardGrantRequest(
            CreateRequestId(mail.Id),
            $"Mail reward {mail.Id}",
            rewards,
            CurrencyChangeReasonType.RewardGrant,
            $"mail:{mail.Id}");
    }

    private static Guid CreateRequestId(long mailId)
    {
        if (mailId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mailId),
                mailId,
                "Mail id must be greater than zero.");
        }

        Span<byte> requestIdBytes = stackalloc byte[16];
        MailRewardNamespaceId.TryWriteBytes(requestIdBytes);
        BinaryPrimitives.WriteInt64LittleEndian(
            requestIdBytes[8..],
            mailId);

        return new Guid(requestIdBytes);
    }
}
