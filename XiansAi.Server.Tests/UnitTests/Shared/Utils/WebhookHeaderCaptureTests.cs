using Microsoft.AspNetCore.Http;
using Shared.Utils;

namespace XiansAi.Server.Tests.UnitTests.Shared.Utils;

public class WebhookHeaderCaptureTests
{
    [Fact]
    public void CapturePreservesHeadersByDefault()
    {
        var headers = new HeaderDictionary { ["X-Service-Signature"] = "value" };

        var result = WebhookHeaderCapture.Capture(headers);

        Assert.Equal("value", result!["X-Service-Signature"]);
    }

    [Fact]
    public void CaptureExcludesSensitiveHeaders()
    {
        var headers = new HeaderDictionary
        {
            ["Content-Type"] = "application/json",
            ["Authorization"] = "Bearer secret",
            ["X-Auth"] = "secret",
            ["X-Service-Token"] = "secret",
            ["X-Service-Signature"] = "secret"
        };

        var result = WebhookHeaderCapture.Capture(headers, excludeSensitive: true);

        Assert.Equal("application/json", result!["Content-Type"]);
        Assert.Single(result);
    }
}
