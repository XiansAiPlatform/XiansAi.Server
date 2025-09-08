using System.Net;

namespace Shared.Models;

/// <summary>
/// A serializable alternative to HttpResponseMessage for webhook responses
/// </summary>
public class WebhookResponse
{
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public Dictionary<string, string[]> Headers { get; set; } = new();
    public string? Content { get; set; }
    public string? ContentType { get; set; }

    public WebhookResponse()
    {
    }

    public WebhookResponse(HttpStatusCode statusCode, string? content = null, string? contentType = "application/json")
    {
        StatusCode = statusCode;
        Content = content;
        ContentType = contentType;
    }

    /// <summary>
    /// Creates a WebhookResponse from an HttpResponseMessage
    /// </summary>
    public static async Task<WebhookResponse> FromHttpResponseMessageAsync(HttpResponseMessage httpResponse)
    {
        var response = new WebhookResponse
        {
            StatusCode = httpResponse.StatusCode
        };

        // Copy headers
        foreach (var header in httpResponse.Headers)
        {
            response.Headers[header.Key] = header.Value.ToArray();
        }

        // Copy content and content headers
        if (httpResponse.Content != null)
        {
            response.Content = await httpResponse.Content.ReadAsStringAsync();
            response.ContentType = httpResponse.Content.Headers.ContentType?.ToString();

            foreach (var header in httpResponse.Content.Headers)
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        return response;
    }

    /// <summary>
    /// Applies this WebhookResponse to an HttpContext.Response
    /// </summary>
    public async Task ApplyToHttpContextAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = (int)StatusCode;

        // Apply headers
        foreach (var header in Headers)
        {
            httpContext.Response.Headers.TryAdd(header.Key, header.Value);
        }

        // Apply content
        if (!string.IsNullOrEmpty(Content))
        {
            if (!string.IsNullOrEmpty(ContentType))
            {
                httpContext.Response.ContentType = ContentType;
            }
            await httpContext.Response.WriteAsync(Content);
        }
    }
}
