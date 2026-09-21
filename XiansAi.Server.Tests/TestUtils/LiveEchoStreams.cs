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
/// Live Admin SSE and UserApi SignalR subscriptions used by the Echo Lib cycle.
/// Both are fed by <c>MongoChangeStreamService</c> when an outgoing chat message is inserted.
/// </summary>
public static class LiveEchoStreams
{
    public static async Task<AdminSseSession> ListenAdminAsync(
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
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/admin/tenants/{tenantId}/messaging/listen?{query}");

        var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Admin SSE listen returned {(int)response.StatusCode}: {body}");
        }

        var session = new AdminSseSession(client, response);
        await session.WaitForConnectedAsync(cancellationToken);
        return session;
    }

    public static async Task<ChatHubSession> ConnectTenantChatHubAsync(
        TestServer server,
        string apiKey,
        string tenantId,
        string workflowId,
        CancellationToken cancellationToken)
    {
        var hubUrl =
            $"http://localhost/ws/tenant/chat?tenantId={Uri.EscapeDataString(tenantId)}" +
            $"&apikey={Uri.EscapeDataString(apiKey)}";
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
                $"Tenant chat hub did not stay connected ({connection.State}). {closeReason}");
        }

        await connection.InvokeAsync("SubscribeToAgent", workflowId, tenantId, cancellationToken);
        return session;
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
}

public sealed class AdminSseSession : IAsyncDisposable
{
    private readonly HttpClient _client;
    private readonly HttpResponseMessage _response;
    private readonly StreamReader _reader;
    private readonly StringBuilder _buffer = new();

    internal AdminSseSession(HttpClient client, HttpResponseMessage response)
    {
        _client = client;
        _response = response;
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
                    $"Admin SSE stream ended before seeing '{text}'. Buffer: {_buffer}");
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
