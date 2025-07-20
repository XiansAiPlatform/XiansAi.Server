using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;
using Shared.Data;
using Shared.Services;
using Microsoft.AspNetCore.Authentication;
using Shared.Auth;
using Microsoft.Extensions.Logging;

namespace XiansAi.Server.Tests.TestUtils;

public class XiansAiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly MongoDbFixture _mongoFixture;
    private const string TestTenantId = "test-tenant";
    private readonly string? _environment;

    public XiansAiWebApplicationFactory(MongoDbFixture mongoFixture, string? environment = null)
    {
        _mongoFixture = mongoFixture;
        _environment = environment;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set the environment if specified
        if (!string.IsNullOrEmpty(_environment))
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _environment);
            builder.UseEnvironment(_environment);
        }

        // Configure additional configuration sources for testing
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Clear existing configuration sources to ensure test configuration takes precedence
            config.Sources.Clear();
            
            // Add base configuration
            config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            
            // Add environment-specific configuration
            var env = context.HostingEnvironment.EnvironmentName;
            config.AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false);
            
                            // Add test-specific overrides
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Override MongoDB settings to use test database
                    ["MongoDB:ConnectionString"] = _mongoFixture.MongoConfig.ConnectionString,
                    ["MongoDB:DatabaseName"] = _mongoFixture.MongoConfig.DatabaseName,
                    
                    // Configure cache provider for testing
                    ["Cache:Provider"] = "memory",
                    ["Cache:Memory:SizeLimit"] = "1024",
                    ["Cache:Memory:ExpirationScanFrequency"] = "00:00:30",
                    
                    // Override other settings for testing as needed
                    ["Logging:LogLevel:Default"] = "Information",
                    ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning"
                });
            
            // Add environment variables
            config.AddEnvironmentVariables();
        });

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

            // Remove existing cache provider
            RemoveService<XiansAi.Server.Providers.ICacheProvider>(services);

            // Configure cache provider for testing
            services.AddMemoryCache();
            services.AddScoped<XiansAi.Server.Providers.ICacheProvider, XiansAi.Server.Providers.InMemoryCacheProvider>();

            // Override JWT authentication with test authentication for WebApi endpoints
            services.AddAuthentication("Test")
                .AddScheme<TestAuthenticationOptions, TestAuthHandler>("Test", options => { });

            // Override TenantContext for testing
            services.AddScoped<ITenantContext>(sp => 
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var logger = sp.GetRequiredService<ILogger<TenantContext>>();
                return new TenantContext(configuration, logger)
                {
                    TenantId = TestTenantId,
                    LoggedInUser = "test-user",
                    UserRoles = new[] { "User" },
                    AuthorizedTenantIds = new[] { TestTenantId }
                };
            });
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