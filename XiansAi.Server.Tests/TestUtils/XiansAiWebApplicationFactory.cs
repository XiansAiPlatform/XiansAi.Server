using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using XiansAi.Server.Database;
using Features.Shared.Auth;
using Features.AgentApi.Auth;

namespace XiansAi.Server.Tests.TestUtils;

public class XiansAiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly MongoDbFixture _mongoFixture;

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
            RemoveService<ITenantContext>(services);
            
            // Add test services
            services.AddSingleton<IMongoDbClientService>(_mongoFixture.MongoClientService);
            services.AddSingleton(_mongoFixture.TenantContextMock.Object);
            
            // Do not replace the authentication system - use the one from Program.cs
            // which already has Certificate authentication configured
        });
    }

    private void RemoveService<T>(IServiceCollection services) where T : class
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }
    }
} 