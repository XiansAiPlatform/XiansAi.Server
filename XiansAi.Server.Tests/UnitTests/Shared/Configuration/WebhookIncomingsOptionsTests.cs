using Microsoft.Extensions.Options;
using Shared.Configuration;
using Xunit;

namespace Tests.UnitTests.Shared.Configuration;

public class WebhookIncomingsOptionsTests
{
    private static ValidateOptionsResult Validate(WebhookIncomingsOptions options) =>
        new DataAnnotationValidateOptions<WebhookIncomingsOptions>(null).Validate(null, options);

    [Fact]
    public void DefaultsAreValid()
    {
        Assert.True(Validate(new WebhookIncomingsOptions()).Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxPerWebhookMustBePositive(int value)
    {
        Assert.True(Validate(new WebhookIncomingsOptions { MaxPerWebhook = value }).Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(WebhookIncomingsOptions.MaxStoredBodyBytesLimit + 1)]
    public void MaxStoredBodyBytesMustFitInADocument(int value)
    {
        Assert.True(Validate(new WebhookIncomingsOptions { MaxStoredBodyBytes = value }).Failed);
    }
}
