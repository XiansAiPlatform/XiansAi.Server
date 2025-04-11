using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace XiansAi.Server.Tests.TestUtils;

public class RetryHttpClient : IAsyncDisposable
{
    private readonly HttpClient _innerClient;
    private readonly Action _configureCertificate;
    private readonly int _maxRetries;
    private readonly int _retryDelayMs;

    public HttpClient HttpClient => _innerClient;

    public RetryHttpClient(
        HttpClient innerClient,
        Action configureCertificate,
        int maxRetries = 3,
        int retryDelayMs = 1000)
    {
        _innerClient = innerClient;
        _configureCertificate = configureCertificate;
        _maxRetries = maxRetries;
        _retryDelayMs = retryDelayMs;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage? response = null;
        int retryCount = 0;

        while (retryCount <= _maxRetries)
        {
            response = await _innerClient.SendAsync(request, cancellationToken);
            
            if (response.StatusCode != HttpStatusCode.Unauthorized || retryCount == _maxRetries)
            {
                return response;
            }

            retryCount++;
            if (retryCount <= _maxRetries)
            {
                await Task.Delay(_retryDelayMs, cancellationToken);
                _configureCertificate();
            }
        }
        if (response == null)
        {
            throw new InvalidOperationException("Failed to execute request after retries");
        }

        return response;
    }

    public Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken = default)
    {
        return SendAsync(new HttpRequestMessage(HttpMethod.Get, requestUri), cancellationToken);
    }

    public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
        return SendAsync(request, cancellationToken);
    }

    public Task<HttpResponseMessage> PutAsync(string requestUri, HttpContent content, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, requestUri) { Content = content };
        return SendAsync(request, cancellationToken);
    }

    public Task<HttpResponseMessage> DeleteAsync(string requestUri, CancellationToken cancellationToken = default)
    {
        return SendAsync(new HttpRequestMessage(HttpMethod.Delete, requestUri), cancellationToken);
    }

    public Task<HttpResponseMessage> PostAsJsonAsync<TValue>(string requestUri, TValue value, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        var content = JsonContent.Create(value, options: options);
        return PostAsync(requestUri, content, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _innerClient.Dispose();
        await ValueTask.CompletedTask;
    }
} 