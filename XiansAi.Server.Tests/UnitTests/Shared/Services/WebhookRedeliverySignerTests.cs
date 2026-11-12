using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Services;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

public class WebhookRedeliverySignerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ASignedClaimVerifiesForItsTenant()
    {
        var signer = Signer();
        var claim = Claim();

        Assert.Equal(claim, signer.Verify(signer.Sign(claim), "tenant"));
    }

    [Fact]
    public void AClaimForAnotherTenantIsRejected()
    {
        var signer = Signer();

        Assert.Null(signer.Verify(signer.Sign(Claim()), "other-tenant"));
    }

    [Fact]
    public void AnExpiredClaimIsRejected()
    {
        var token = Signer().Sign(Claim());

        Assert.Null(Signer(now: Now.AddMinutes(6)).Verify(token, "tenant"));
    }

    [Fact]
    public void AChangedPayloadIsRejected()
    {
        var signer = Signer();
        var signature = signer.Sign(Claim()).Split('.')[1];
        var forged = signer.Sign(Claim() with { TriggeredBy = "someone-else" }).Split('.')[0];

        Assert.Null(signer.Verify($"{forged}.{signature}", "tenant"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-dot")]
    [InlineData("a.b.c")]
    [InlineData("!!!.???")]
    public void AMalformedTokenIsRejected(string? token)
    {
        Assert.Null(Signer().Verify(token, "tenant"));
    }

    [Fact]
    public void AClaimSignedWithAnotherKeyIsRejected()
    {
        var token = Signer(secret: "another-secret-another-secret-another").Sign(Claim());

        Assert.Null(Signer().Verify(token, "tenant"));
    }

    [Fact]
    public void TheDedicatedKeyIsPreferredOverTheBaseSecret()
    {
        var withDedicatedKey = new WebhookRedeliverySigner(
            Configuration(("EncryptionKeys:BaseSecret", "base-secret-base-secret-base-secret"),
                ("EncryptionKeys:UniqueSecrets:WebhookRedeliveryKey", "dedicated-dedicated-dedicated-key")),
            NullLogger<WebhookRedeliverySigner>.Instance, new FixedTime(Now));

        Assert.Null(Signer(secret: "base-secret-base-secret-base-secret").Verify(withDedicatedKey.Sign(Claim()), "tenant"));
    }

    [Fact]
    public void WithoutAKeySigningThrowsAndNothingVerifies()
    {
        var signer = new WebhookRedeliverySigner(Configuration(), NullLogger<WebhookRedeliverySigner>.Instance, new FixedTime(Now));

        Assert.Throws<InvalidOperationException>(() => signer.Sign(Claim()));
        Assert.Null(signer.Verify(Signer().Sign(Claim()), "tenant"));
    }

    private static WebhookRedeliverySigner Signer(string secret = "base-secret-base-secret-base-secret", DateTimeOffset? now = null) =>
        new(Configuration(("EncryptionKeys:BaseSecret", secret)), NullLogger<WebhookRedeliverySigner>.Instance, new FixedTime(now ?? Now));

    private static WebhookRedeliveryClaim Claim() => new(
        TenantId: "tenant",
        IntegrationId: "integration",
        RedeliveryOf: Guid.Parse("0193f0a0-0000-7000-8000-000000000001"),
        RedeliveryOfRequestId: "request-1",
        DeliveryId: Guid.Parse("0193f0a0-0000-7000-8000-000000000002"),
        TriggeredBy: "dev@example.com",
        ExpiresAtUnixSeconds: Now.Add(WebhookRedeliverySigner.Lifetime).ToUnixTimeSeconds(),
        BodySha256: WebhookRedeliverySigner.HashBody("{}"));

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
