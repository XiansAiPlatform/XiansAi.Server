using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Features.WebApi.Services;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

public static class TemplateEndpoints
{
    public static void MapTemplateEndpoints(this WebApplication app)
    {
        // Map template endpoints with common attributes
        var templateGroup = app.MapGroup("/api/client/templates")
            .WithTags("WebAPI - Templates")
            .RequiresValidTenant()
            .RequireAuthorization();

        templateGroup.MapGet("/agents", async (
            [FromQuery] bool? basicDataOnly,
            [FromServices] ITemplateService service) =>
        {
            var result = await service.GetSystemScopedAgentDefinitions(basicDataOnly ?? false);
            return result.ToHttpResult();
        })
        .WithName("Get System-Scoped Agent Definitions")
        .WithOpenApi(operation => {
            operation.Summary = "Get system-scoped agent definitions";
            operation.Description = "Retrieves all agent definitions that have system_scoped attribute set to true";
            return operation;
        });
    }
}
