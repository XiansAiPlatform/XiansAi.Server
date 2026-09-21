using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;

namespace Tests.TestUtils;

/// <summary>
/// Live Admin/UserApi SSE and SignalR subscriptions used by the Echo Lib cycle.
/// All are fed by <c>MongoChangeStreamService</c> when an outgoing chat message is inserted.
/// </summary>
public static class LiveEchoStreams
{
    public static Task<SseSession> ListenAdminAsync(
        HttpClient client,
        string tenantId,
        string agentName,
        string activationName,
        string participantId,
        CancellationToken cancellationToken)
    {
        var query =
            $"agentName={Uri.EscapeDataString(agentName)}" +
            $"&activationName={Uri.EscapeDataString(activationName)}" +
            $"&participantId={Uri.EscapeDataString(participantId)}" +
            "&heartbeatSeconds=300";
        return ListenSseAsync(
            client,
            $"/api/v1/admin/tenants/{tenantId}/messaging/listen?{query}",
            "Admin SSE",
            cancellationToken);
    }

    public static Task<SseSession> ListenUserApiAsync(
        HttpClient client,
        string tenantId,
        string workflowId,
        string participantId,
        CancellationToken cancellationToken)
    {
        var query =
            $"workflow={Uri.EscapeDataString(workflowId)}" +
            $"&participantId={Uri.EscapeDataString(participantId)}" +
            $"&tenantId={Uri.EscapeDataString(tenantId)}" +
            "&heartbeatSeconds=300";
        return ListenSseAsync(
            client,
            $"/api/user/sse/events?{query}",
            "UserApi SSE",
            cancellationToken);
    }

    public static Task<ChatHubSession> ConnectTenantChatHubAsync(
        TestServer server,
        string apiKey,
        string tenantId,
        string workflowId,
        CancellationToken cancellationToken)
    {
        var hubUrl =
            $"http://localhost/ws/tenant/chat?tenantId={Uri.EscapeDataString(tenantId)}" +
            $"&apikey={Uri.EscapeDataString(apiKey)}";
        return ConnectHubAsync(
            server,
            hubUrl,
            "Tenant chat hub",
            (connection, ct) => connection.InvokeAsync("SubscribeToAgent", workflowId, tenantId, ct),
            cancellationToken);
    }

    public static Task<ChatHubSession> ConnectChatHubAsync(
        TestServer server,
        string apiKey,
        string tenantId,
        string workflowId,
        string participantId,
        CancellationToken cancellationToken)
    {
        var hubUrl =
            $"http://localhost/ws/chat?tenantId={Uri.EscapeDataString(tenantId)}" +
            $"&apikey={Uri.EscapeDataString(apiKey)}";
        return ConnectHubAsync(
            server,
            hubUrl,
            "Chat hub",
            (connection, ct) => connection.InvokeAsync(
                "SubscribeToAgent",
                workflowId,
                participantId,
                tenantId,
                ct),
            cancellationToken);
    }

    public static HttpClient CreateStreamingClient(XiansAiWebApplicationFactory factory, string apiKey, string tenantId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client;
    }

    private static async Task<SseSession> ListenSseAsync(
        HttpClient client,
        string url,
        string channel,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"{channel} listen returned {(int)response.StatusCode}: {body}");
        }

        var session = new SseSession(client, response, channel);
        await session.WaitForConnectedAsync(cancellationToken);
        return session;
    }

    private static async Task<ChatHubSession> ConnectHubAsync(
        TestServer server,
        string hubUrl,
        string channel,
        Func<HubConnection, CancellationToken, Task> subscribe,
        CancellationToken cancellationToken)
    {
        var innerHandler = server.CreateHandler();
        var closeReason = new StringBuilder();
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => new NonDisposingHandler(innerHandler);
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        connection.Closed += error =>
        {
            closeReason.Append(error?.Message ?? "closed");
            return Task.CompletedTask;
        };
        connection.On<object>("ConnectionError", payload =>
            closeReason.Append(JsonSerializer.Serialize(payload)));
        connection.On<string>("Error", message => closeReason.Append(message));

        var session = new ChatHubSession(connection);
        await connection.StartAsync(cancellationToken);
        if (connection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException(
                $"{channel} did not stay connected ({connection.State}). {closeReason}");
        }

        await subscribe(connection, cancellationToken);
        return session;
    }
}

public sealed class SseSession : IAsyncDisposable
{
    private readonly HttpClient _client;
    private readonly HttpResponseMessage _response;
    private readonly StreamReader _reader;
    private readonly StringBuilder _buffer = new();
    private readonly string _channel;

    internal SseSession(HttpClient client, HttpResponseMessage response, string channel)
    {
        _client = client;
        _response = response;
        _channel = channel;
        _reader = new StreamReader(response.Content.ReadAsStream());
    }

    public string Buffer => _buffer.ToString();

    internal async Task WaitForConnectedAsync(CancellationToken cancellationToken)
    {
        await WaitForTextAsync("event: connected", cancellationToken);
    }

    public async Task WaitForTextAsync(string text, CancellationToken cancellationToken)
    {
        if (_buffer.ToString().Contains(text, StringComparison.Ordinal))
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                throw new InvalidOperationException(
                    $"{_channel} stream ended before seeing '{text}'. Buffer: {_buffer}");
            }

            _buffer.AppendLine(line);
            if (_buffer.ToString().Contains(text, StringComparison.Ordinal))
            {
                return;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        _response.Dispose();
        _client.Dispose();
        await ValueTask.CompletedTask;
    }
}

public sealed class ChatHubSession : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly StringBuilder _received = new();

    internal ChatHubSession(HubConnection connection)
    {
        _connection = connection;
        _connection.On<JsonElement>("ReceiveChat", OnChat);
        _connection.On<JsonElement>("ReceiveMessage", OnChat);
    }

    public string Received => _received.ToString();

    public async Task WaitForTextAsync(string text, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_received.ToString().Contains(text, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new InvalidOperationException(
            $"SignalR ReceiveChat did not contain '{text}'. Payload: {_received}");
    }

    private void OnChat(JsonElement message)
    {
        _received.AppendLine(message.GetRawText());
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _connection.StopAsync();
        }
        catch (Exception)
        {
            // Hub shutdown is best-effort.
        }

        await _connection.DisposeAsync();
    }
}

/// <summary>
/// SignalR disposes handlers it creates; the TestServer handler must stay with the factory.
/// </summary>
internal sealed class NonDisposingHandler : DelegatingHandler
{
    public NonDisposingHandler(HttpMessageHandler inner) : base(inner)
    {
    }

    protected override void Dispose(bool disposing)
    {
    }
}
