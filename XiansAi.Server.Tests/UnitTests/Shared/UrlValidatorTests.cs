using Xunit;
using Shared.Utils;

namespace XiansAi.Server.Tests.UnitTests.Shared;

public class UrlValidatorTests
{
    [Theory]
    [InlineData("https://github.com/webhook", true, "")]
    [InlineData("http://google.com/webhook", true, "")]
    [InlineData("https://api.github.com/callback", true, "")]
    [InlineData("https://www.cloudflare.com:443/path", true, "")]
    public void IsValidWebhookUrl_ValidPublicUrls_ReturnsTrue(string url, bool expectedResult, string expectedError)
    {
        // Act
        var result = UrlValidator.IsValidWebhookUrl(url, out var errorMessage);

        // Assert
        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedError, errorMessage);
    }

    [Theory]
    [InlineData("http://localhost/webhook")]
    [InlineData("http://localhost:8080/webhook")]
    [InlineData("https://localhost/webhook")]
    [InlineData("http://127.0.0.1/webhook")]
    [InlineData("http://127.0.0.2/webhook")]
    [InlineData("http://127.255.255.255/webhook")]
    public void IsValidWebhookUrl_LocalhostUrls_ReturnsFalse(string url)
    {
        // Act
        var result = UrlValidator.IsValidWebhookUrl(url, out var errorMessage);

        // Assert
        Assert.False(result);
        Assert.NotEmpty(errorMessage);
    }

    [Theory]
    [InlineData("http://10.0.0.1/webhook")] // Private network
    [InlineData("http://10.255.255.255/webhook")]
    [InlineData("http://172.16.0.1/webhook")]
    [InlineData("http://172.31.255.255/webhook")]
    [InlineData("http://192.168.1.1/webhook")]
    [InlineData("http://192.168.255.255/webhook")]
    public void IsValidWebhookUrl_PrivateIpRanges_ReturnsFalse(string url)
    {
        // Act
        var result = UrlValidator.IsValidWebhookUrl(url, out var errorMessage);

        // Assert
        Assert.False(result);
        Assert.Contains("private", errorMessage.ToLower());
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")] // AWS metadata
    [InlineData("http://169.254.169.254/latest/user-data/")]
    [InlineData("http://169.254.0.1/webhook")]
    public void IsValidWebhookUrl_CloudMetadataEndpoints_ReturnsFalse(string url)
    {
        // Act
        var result = UrlValidator.IsValidWebhookUrl(url, out var errorMessage);

        // Assert
        Assert.False(result);
        Assert.NotEmpty(errorMessage);
    }

    [Theory]
    [InlineData("http://metadata.google.internal/computeMetadata/v1/")] // GCP metadata
    public void IsValidWebhookUrl_GcpMetadataEndpoint_ReturnsFalse(string url)
    {
        // Act
        var result = UrlValidator.IsValidWebhookUrl(url, out var errorMessage);

        // Assert
        Assert.False(result);
        Assert.Contains("not allowed", errorMessage.ToLower());
    }

    [Theory]
    [InlineData("ftp://example.com/webhook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com")]
    [InlineData("javascript:alert(1)")]
    public void IsValidWebhookUrl_InvalidSchemes_ReturnsFalse(string url)
    {
        // Act
        var result = UrlValidator.IsValidWebhookUrl(url, out var errorMessage);

        // Assert
        Assert.False(result);
        Assert.Contains("scheme", errorMessage.ToLower());
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData("://invalid")]
    public void IsValidWebhookUrl_InvalidUrlFormat_ReturnsFalse(string url)
    {
        // Act
        var result = UrlValidator.IsValidWebhookUrl(url, out var errorMessage);

        // Assert
        Assert.False(result);
        Assert.NotEmpty(errorMessage);
    }

    [Fact]
    public void IsValidWebhookUrl_IPv6Localhost_ReturnsFalse()
    {
        // Arrange
        var url = "http://[::1]/webhook";

        // Act
        var result = UrlValidator.IsValidWebhookUrl(url, out var errorMessage);

        // Assert
        Assert.False(result);
        Assert.NotEmpty(errorMessage);
    }

    [Fact]
    public void IsValidWebhookUrl_IPv6LinkLocal_ReturnsFalse()
    {
        // Arrange
        var url = "http://[fe80::1]/webhook";

        // Act
        var result = UrlValidator.IsValidWebhookUrl(url, out var errorMessage);

        // Assert
        Assert.False(result);
        Assert.NotEmpty(errorMessage);
    }
}

