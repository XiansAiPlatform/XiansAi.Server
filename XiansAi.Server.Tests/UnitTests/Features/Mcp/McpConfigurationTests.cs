using Features.Mcp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DocumentService = Features.AgentApi.Services.IDocumentService;
using DocumentServiceImplementation = Features.AgentApi.Services.DocumentService;

namespace XiansAi.Server.Tests.UnitTests.Features.Mcp;

public class McpConfigurationTests
{
    [Fact]
    public void RegistersDocumentServiceWithoutAgentApi()
    {
        var services = new ServiceCollection();
        services.AddXiansMcp();
        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(DocumentService));
        Assert.Equal(typeof(DocumentServiceImplementation), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void PreservesExistingDocumentServiceRegistration()
    {
        IServiceCollection services = new ServiceCollection();
        var existing = ServiceDescriptor.Scoped<DocumentService, DocumentServiceImplementation>();
        services.Add(existing);
        services.AddXiansMcp();
        Assert.Same(existing, Assert.Single(services, service => service.ServiceType == typeof(DocumentService)));
    }
}
