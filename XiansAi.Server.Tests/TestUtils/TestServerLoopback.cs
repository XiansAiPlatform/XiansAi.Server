using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.TestHost;

namespace Tests.TestUtils;

/// <summary>
/// Forwards real TCP HTTP to the in-process <see cref="TestServer"/> so out-of-process
/// clients such as Xians.Lib can reach the WebApplicationFactory host.
/// </summary>
public sealed class TestServerLoopback : IAsyncDisposable
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Connection",
        "Transfer-Encoding",
        "TE",
        "Trailer",
        "Upgrade",
        "Host",
        "Content-Length"
    };

    private readonly HttpListener _listener;
    private readonly HttpClient _inner;
    private readonly CancellationTokenSource _cts;
    private readonly Task _acceptLoop;

    private TestServerLoopback(HttpListener listener, HttpClient inner, CancellationTokenSource cts, Task acceptLoop, string baseAddress)
    {
        _listener = listener;
        _inner = inner;
        _cts = cts;
        _acceptLoop = acceptLoop;
        BaseAddress = baseAddress;
    }

    public string BaseAddress { get; }

    public static TestServerLoopback Start(TestServer server)
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var inner = new HttpClient(server.CreateHandler(), disposeHandler: false)
        {
            BaseAddress = new Uri("http://localhost")
        };

        var cts = new CancellationTokenSource();
        var loop = AcceptLoopAsync(listener, inner, cts.Token);
        return new TestServerLoopback(listener, inner, cts, loop, prefix.TrimEnd('/'));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Close();
        try
        {
            await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Listener shutdown races with in-flight forwards.
        }

        _inner.Dispose();
        _cts.Dispose();
    }

    private static async Task AcceptLoopAsync(HttpListener listener, HttpClient inner, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || !listener.IsListening)
            {
                return;
            }

            _ = Task.Run(() => ForwardAsync(context, inner), cancellationToken);
        }
    }

    private static async Task ForwardAsync(HttpListenerContext context, HttpClient inner)
    {
        try
        {
            var request = context.Request;
            using var outgoing = new HttpRequestMessage(
                new HttpMethod(request.HttpMethod),
                request.Url?.PathAndQuery ?? "/");

            foreach (var headerName in request.Headers.AllKeys)
            {
                if (headerName == null || HopByHopHeaders.Contains(headerName))
                {
                    continue;
                }

                var value = request.Headers[headerName];
                if (value == null)
                {
                    continue;
                }

                if (!outgoing.Headers.TryAddWithoutValidation(headerName, value))
                {
                    outgoing.Content ??= new StreamContent(Stream.Null);
                    outgoing.Content.Headers.TryAddWithoutValidation(headerName, value);
                }
            }

            if (request.ContentLength64 > 0 || request.HasEntityBody)
            {
                using var body = new MemoryStream();
                await request.InputStream.CopyToAsync(body);
                outgoing.Content = new ByteArrayContent(body.ToArray());
                if (!string.IsNullOrWhiteSpace(request.ContentType))
                {
                    outgoing.Content.Headers.TryAddWithoutValidation("Content-Type", request.ContentType);
                }
            }

            using var response = await inner.SendAsync(outgoing);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers)
            {
                if (HopByHopHeaders.Contains(header.Key))
                {
                    continue;
                }

                context.Response.Headers[header.Key] = string.Join(",", header.Value);
            }

            foreach (var header in response.Content.Headers)
            {
                if (HopByHopHeaders.Contains(header.Key))
                {
                    continue;
                }

                context.Response.Headers[header.Key] = string.Join(",", header.Value);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception)
        {
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            }
            catch (Exception)
            {
                // Response may already be closed.
            }
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception)
            {
                // Ignore close races.
            }
        }
    }

    private static int GetFreePort()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }
}
