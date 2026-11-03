using Shared.Services.Privacy;
using Xunit;

namespace Tests.UnitTests.Shared.Privacy;

public class PrivacyErrorTextTests
{
    [Fact]
    public void PrivacyRequestException_KeepsItsMessage()
    {
        Assert.Equal("Data must be handled manually.",
            PrivacyErrorText.Describe(new PrivacyRequestException("Data must be handled manually.")));
    }

    [Fact]
    public void OtherExceptions_KeepOnlyTheirType()
    {
        var error = new InvalidOperationException("E11000 duplicate key error dup key: { email: \"alice@example.com\" }");

        Assert.Equal("System.InvalidOperationException", PrivacyErrorText.Describe(error));
    }
}
