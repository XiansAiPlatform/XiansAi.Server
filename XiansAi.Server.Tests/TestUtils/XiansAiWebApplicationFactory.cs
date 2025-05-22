using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shared.Data;
using Shared.Utils.GenAi;
using Moq;

namespace XiansAi.Server.Tests.TestUtils;

public class XiansAiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly MongoDbFixture _mongoFixture;
    private const string TestTenantId = "test-tenant";

    public XiansAiWebApplicationFactory(MongoDbFixture mongoFixture)
    {
        _mongoFixture = mongoFixture;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Remove existing MongoDB services
            RemoveService<IMongoDbClientService>(services);
            RemoveService<HttpClient>(services);
            RemoveService<IMarkdownService>(services);

            // Add test services
            services.AddSingleton<IMongoDbClientService>(_mongoFixture.MongoClientService);
            
            // Add mock HTTP client that always returns success
            services.AddScoped<HttpClient>(sp => 
            {
                var handler = new TestHttpMessageHandler();
                return new HttpClient(handler);
            });
            
            // Add mock MarkdownService
            var mockMarkdownService = new Mock<IMarkdownService>();
            mockMarkdownService.Setup(m => m.GenerateMarkdown(It.IsAny<string>()))
                .ReturnsAsync("```mermaid\ngraph TD\n    A[Start] --> B[End]\n```");
            services.AddSingleton<IMarkdownService>(mockMarkdownService.Object);
        });
    }

    private void RemoveService<T>(IServiceCollection services) where T : class
    {
        var descriptors = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }
}
public class TestHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
} 