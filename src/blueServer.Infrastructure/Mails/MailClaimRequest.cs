namespace blueServer.Infrastructure.Mails;

// 부분 수령 한 번을 식별한다. 동일 작업의 재시도에서는 같은 RequestId를 사용한다.
public sealed record MailClaimRequest
{
    public long MailId { get; }
    public Guid RequestId { get; }

    public MailClaimRequest(long mailId, Guid requestId)
    {
        if (mailId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mailId),
                mailId,
                "Mail id must be greater than zero.");
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Claim request id must not be empty.",
                nameof(requestId));
        }

        MailId = mailId;
        RequestId = requestId;
    }
}
