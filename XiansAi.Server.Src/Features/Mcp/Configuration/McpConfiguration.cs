using Features.Mcp.Tools;

namespace Features.Mcp.Configuration;

public static class McpConfiguration
{
    public static void AddXiansMcp(this IServiceCollection services)
    {
        services.AddScoped<Features.AgentApi.Services.IDocumentService, Features.AgentApi.Services.DocumentService>();
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
