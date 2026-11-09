using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace Shared.Services;

/// <summary>
/// What a redelivery tells the builtin webhook endpoint about itself: which recorded call it
/// repeats, the id to save it under and who asked for it.
/// </summary>
public record WebhookRedeliveryClaim(
    [property: JsonPropertyName("t")] string TenantId,
    [property: JsonPropertyName("i")] string IntegrationId,
    [property: JsonPropertyName("o")] Guid RedeliveryOf,
    [property: JsonPropertyName("r")] string RedeliveryOfRequestId,
    [property: JsonPropertyName("n")] Guid DeliveryId,
    [property: JsonPropertyName("u")] string? TriggeredBy,
    [property: JsonPropertyName("e")] long ExpiresAtUnixSeconds);

/// <summary>
/// Signs and checks the <see cref="TokenHeader"/> value a redelivery is sent with. Anyone can POST
/// to a webhook URL, so the endpoint trusts a redelivery's claim only when its signature verifies.
/// </summary>
public interface IWebhookRedeliverySigner
{
    /// <summary>Signs <paramref name="claim"/> for the <see cref="WebhookRedeliverySigner.TokenHeader"/> header.</summary>
    /// <exception cref="InvalidOperationException">No signing key is configured.</exception>
    string Sign(WebhookRedeliveryClaim claim);

    /// <summary>
    /// The claim in <paramref name="token"/> when it was signed by this server, has not expired
    /// and belongs to <paramref name="tenantId"/>; otherwise null.
    /// </summary>
    WebhookRedeliveryClaim? Verify(string? token, string tenantId);
}

public class WebhookRedeliverySigner : IWebhookRedeliverySigner
{
    /// <summary>Carries the signed claim. Never stored with the call.</summary>
    public const string TokenHeader = "X-Xians-Redelivery";

    /// <summary>The id of the delivery being repeated. Stored with the call, so it shows in its headers.</summary>
    public const string RedeliveryOfHeader = "X-Xians-Redelivery-Of";

    /// <summary>How long a signed claim is accepted. The server sends it straight to itself, so this only covers clock skew between instances.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private const string KeyConfig = "EncryptionKeys:UniqueSecrets:WebhookRedeliveryKey";
    private const string BaseSecretConfig = "EncryptionKeys:BaseSecret";

    private readonly byte[]? _key;
    private readonly TimeProvider _time;
    private readonly ILogger<WebhookRedeliverySigner> _logger;

    public WebhookRedeliverySigner(IConfiguration configuration, ILogger<WebhookRedeliverySigner> logger, TimeProvider? time = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;

        var secret = configuration[KeyConfig];
        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = configuration[BaseSecretConfig];
        }

        // Derived rather than used as is, so this key differs from any other use of the same secret.
        _key = string.IsNullOrWhiteSpace(secret)
            ? null
            : HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret.Trim()), 32,
                info: Encoding.UTF8.GetBytes("xians-webhook-redelivery-v1"));
    }

    public string Sign(WebhookRedeliveryClaim claim)
    {
        if (_key == null)
        {
            throw new InvalidOperationException($"Webhook redelivery needs {KeyConfig} or {BaseSecretConfig} to be configured.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(claim);
        return $"{WebEncoders.Base64UrlEncode(payload)}.{WebEncoders.Base64UrlEncode(HMACSHA256.HashData(_key, payload))}";
    }

    public WebhookRedeliveryClaim? Verify(string? token, string tenantId)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        if (_key == null)
        {
            _logger.LogWarning("Ignoring a webhook redelivery claim: no signing key is configured");
            return null;
        }

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2)
                return Reject("it is malformed");

            var payload = WebEncoders.Base64UrlDecode(parts[0]);
            var signature = WebEncoders.Base64UrlDecode(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(_key, payload)))
                return Reject("its signature does not match");

            var claim = JsonSerializer.Deserialize<WebhookRedeliveryClaim>(payload);
            if (claim == null)
                return Reject("it is empty");
            if (claim.TenantId != tenantId)
                return Reject("it belongs to another tenant");
            if (_time.GetUtcNow().ToUnixTimeSeconds() > claim.ExpiresAtUnixSeconds)
                return Reject("it has expired");

            return claim;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return Reject("it is malformed");
        }
    }

    private WebhookRedeliveryClaim? Reject(string reason)
    {
        _logger.LogWarning("Ignoring a webhook redelivery claim because {Reason}; the call is recorded as a new delivery", reason);
        return null;
    }
}
