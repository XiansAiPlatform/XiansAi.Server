using System.Text.Json;
using Shared.Models;

namespace Shared.Utils;

/// <summary>
/// Reads the <see cref="WebhookResponse"/> from an agent's reply to a built-in webhook.
/// It looks in the reply's Data first, then in its Text.
/// Used by the built-in webhook endpoint and by webhook health checks.
/// </summary>
public static class WebhookResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static WebhookResponse? FromSyncResult(object result, ILogger logger)
    {
        var resultType = result.GetType();
        var dataValue = resultType.GetProperty("Data")?.GetValue(result);
        WebhookResponse? webhookResponse = null;

        if (dataValue is WebhookResponse direct)
        {
            webhookResponse = direct;
        }
        else if (dataValue is JsonElement jsonElement)
        {
            try
            {
                webhookResponse = JsonSerializer.Deserialize<WebhookResponse>(jsonElement.GetRawText(), JsonOptions);
                logger.LogDebug("Successfully deserialized WebhookResponse from JsonElement");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize WebhookResponse from JsonElement. Data: {Data}", jsonElement.GetRawText());
            }
        }
        else if (dataValue != null)
        {
            try
            {
                var json = JsonSerializer.Serialize(dataValue);
                webhookResponse = JsonSerializer.Deserialize<WebhookResponse>(json, JsonOptions);
                logger.LogDebug("Successfully deserialized WebhookResponse from object. StatusCode: {StatusCode}", webhookResponse?.StatusCode);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize WebhookResponse from object. Type: {Type}", dataValue.GetType().Name);
            }
        }

        if (webhookResponse == null && resultType.GetProperty("Text")?.GetValue(result) is string textValue
            && !string.IsNullOrEmpty(textValue))
        {
            try
            {
                webhookResponse = JsonSerializer.Deserialize<WebhookResponse>(textValue, JsonOptions);
                logger.LogDebug("Successfully deserialized WebhookResponse from Text field");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize WebhookResponse from Text field. Text: {Text}", textValue);
            }
        }

        return webhookResponse;
    }
}
