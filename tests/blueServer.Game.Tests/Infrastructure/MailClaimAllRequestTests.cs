using blueServer.Infrastructure.Mails;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class MailClaimAllRequestTests
{
    [Fact]
    public void Constructor_PreservesRequestIdentityForRetry()
    {
        var requestId = Guid.NewGuid();

        var request = new MailClaimAllRequest(requestId);

        Assert.Equal(requestId, request.RequestId);
        Assert.Equal(request, new MailClaimAllRequest(requestId));
    }

    [Fact]
    public void Constructor_RejectsEmptyRequestId()
    {
        var exception = Assert.Throws<ArgumentException>(() => new MailClaimAllRequest(Guid.Empty));

        Assert.Equal("requestId", exception.ParamName);
    }
}
