using Features.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Features.Mcp.Configuration;

public static class McpConfiguration
{
    public static void AddXiansMcp(this IServiceCollection services)
    {
        // MCP also runs in WebApi-only mode, where AgentApiConfiguration is not registered.
        services.TryAddScoped<Features.AgentApi.Services.IDocumentService, Features.AgentApi.Services.DocumentService>();
        services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools<ScheduleTools>()
            .WithTools<DataTools>()
            .WithTools<DiscoveryTools>();
    }

    public static void MapXiansMcp(this WebApplication app) => app.MapMcp(
        "/api/v1/admin/mcp")
        .RequireAuthorization("AdminEndpointAuthPolicy");
}
