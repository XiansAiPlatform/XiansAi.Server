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
    [InlineData(1_001)]
    public void MaxPerWebhookMustBeBetweenOneAndAThousand(int value)
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

    [Fact]
    public void LargestCapWithDefaultBodySizeIsAllowed()
    {
        Assert.True(Validate(new WebhookIncomingsOptions { MaxPerWebhook = 1_000 }).Succeeded);
    }

    [Theory]
    [InlineData(1_000, 2 * 1024 * 1024)]
    [InlineData(100, WebhookIncomingsOptions.MaxStoredBodyBytesLimit)]
    public void WorstCaseStoragePerWebhookIsCapped(int maxPerWebhook, int maxStoredBodyBytes)
    {
        var result = Validate(new WebhookIncomingsOptions { MaxPerWebhook = maxPerWebhook, MaxStoredBodyBytes = maxStoredBodyBytes });

        Assert.True(result.Failed);
        Assert.Contains("per webhook", result.FailureMessage);
    }
}
