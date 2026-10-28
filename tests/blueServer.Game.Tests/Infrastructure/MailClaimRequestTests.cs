using blueServer.Infrastructure.Mails;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class MailClaimRequestTests
{
    [Fact]
    public void Constructor_PreservesRequestIdentityForRetry()
    {
        var requestId = Guid.NewGuid();
        var request = new MailClaimRequest(10, requestId);
        var retry = new MailClaimRequest(10, requestId);

        Assert.Equal(10, request.MailId);
        Assert.Equal(requestId, request.RequestId);
        Assert.Equal(request, retry);
    }

    [Fact]
    public void Constructor_AllowsNewClaimRequestForSameMail()
    {
        var firstRequestId = Guid.NewGuid();
        var secondRequestId = Guid.NewGuid();
        var first = new MailClaimRequest(10, firstRequestId);
        var next = new MailClaimRequest(10, secondRequestId);

        Assert.Equal(first.MailId, next.MailId);
        Assert.Equal(secondRequestId, next.RequestId);
        Assert.NotEqual(first, next);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveMailId(long mailId)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MailClaimRequest(mailId, Guid.NewGuid()));

        Assert.Equal("mailId", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsEmptyRequestId()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new MailClaimRequest(10, Guid.Empty));

        Assert.Equal("requestId", exception.ParamName);
    }
}
