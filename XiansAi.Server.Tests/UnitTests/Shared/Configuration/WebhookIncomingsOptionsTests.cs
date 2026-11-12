using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shared.Configuration;

namespace XiansAi.Server.Tests.UnitTests.Shared.Configuration;

public class WebhookIncomingsOptionsTests
{
    private static ValidateOptionsResult Validate(WebhookIncomingsOptions options) =>
        new DataAnnotationValidateOptions<WebhookIncomingsOptions>(null).Validate(null, options);

    [Fact]
    public void DefaultKeepsAHundredCallsPerWebhook()
    {
        var options = new WebhookIncomingsOptions();

        Assert.Equal(100, options.MaxPerWebhook);
        Assert.True(Validate(options).Succeeded);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1_000, true)]
    [InlineData(1_001, false)]
    public void MaxPerWebhookMustBeBetweenOneAndAThousand(int value, bool valid)
    {
        Assert.Equal(valid, Validate(new WebhookIncomingsOptions { MaxPerWebhook = value }).Succeeded);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("http://localhost:5000", true)]
    [InlineData("https://xians.internal/", true)]
    [InlineData("ftp://localhost", false)]
    [InlineData("localhost:5000", false)]
    // A path would be dropped when the webhook path is joined, so it is refused.
    [InlineData("https://host/xians", false)]
    [InlineData("http://localhost:5000/?x=1", false)]
    public void RedeliveryBaseUrlMustBeOnlyASchemeHostAndPort(string? value, bool valid)
    {
        Assert.Equal(valid, Validate(new WebhookIncomingsOptions { RedeliveryBaseUrl = value }).Succeeded);
    }

    [Fact]
    public void MaxPerWebhookBindsFromTheEnvironmentVariableKey()
    {
        // WebhookIncomings__MaxPerWebhook in the environment becomes this key.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["WebhookIncomings:MaxPerWebhook"] = "250" })
            .Build();

        var options = configuration.GetSection(WebhookIncomingsOptions.SectionName).Get<WebhookIncomingsOptions>();

        Assert.Equal(250, options!.MaxPerWebhook);
    }
}
