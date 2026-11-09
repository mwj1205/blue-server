namespace blueServer.Infrastructure.Mails;

// 수령 대상은 서버가 결정한다. 동일한 일괄 작업을 재시도할 때 같은 ID를 사용한다.
public sealed record MailClaimAllRequest
{
    public Guid RequestId { get; }

    public MailClaimAllRequest(Guid requestId)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Claim all request id must not be empty.",
                nameof(requestId));
        }

        RequestId = requestId;
    }
}
