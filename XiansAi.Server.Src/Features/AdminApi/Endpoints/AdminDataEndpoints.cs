using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for document data access and analytics.
/// Provides schema discovery and data retrieval for admin dashboards.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminDataEndpoints
{
    /// <summary>
    /// Maps all AdminApi data endpoints.
    /// </summary>
    public static void MapAdminDataEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var dataGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/data")
            .WithTags("AdminAPI - Data")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // Get available data schema (types and filters)
        dataGroup.MapGet("/schema", async (
            string tenantId,
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] string agentName,
            [FromQuery] string? activationName,
            [FromServices] IAdminDataService dataService,
            CancellationToken cancellationToken) =>
        {
            var request = new AdminDataSchemaRequest
            {
                TenantId = tenantId,
                StartDate = startDate,
                EndDate = endDate,
                AgentName = agentName,
                ActivationName = activationName
            };

            var result = await dataService.GetDataSchemaAsync(request, cancellationToken);
            return result.ToHttpResult();
        })
        .Produces<AdminDataSchemaResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminDataSchema")
        ;

        // Get paginated data
        dataGroup.MapGet("", async (
            string tenantId,
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] string agentName,
            [FromQuery] string dataType,
            [FromServices] IAdminDataService dataService,
            CancellationToken cancellationToken,
            [FromQuery] string? activationName = null,
            [FromQuery] int skip = 0,
            [FromQuery] int limit = 100) =>
        {
            var request = new AdminDataListRequest
            {
                TenantId = tenantId,
                StartDate = startDate,
                EndDate = endDate,
                AgentName = agentName,
                ActivationName = activationName,
                DataType = dataType,
                Skip = skip,
                Limit = limit
            };

            var result = await dataService.GetDataAsync(request, cancellationToken);
            return result.ToHttpResult();
        })
        .Produces<AdminDataListResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminData")
        ;

        // Delete data by type and filters
        dataGroup.MapDelete("", async (
            string tenantId,
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] string agentName,
            [FromQuery] string dataType,
            [FromServices] IAdminDataService dataService,
            CancellationToken cancellationToken,
            [FromQuery] string? activationName = null) =>
        {
            var request = new AdminDataDeleteRequest
            {
                TenantId = tenantId,
                StartDate = startDate,
                EndDate = endDate,
                AgentName = agentName,
                ActivationName = activationName,
                DataType = dataType
            };

            var result = await dataService.DeleteDataAsync(request, cancellationToken);
            return result.ToHttpResult();
        })
        .Produces<AdminDataDeleteResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("DeleteAdminData")
        ;

        // Delete a specific record by ID
        dataGroup.MapDelete("/{recordId}", async (
            string tenantId,
            string recordId,
            [FromServices] IAdminDataService dataService,
            CancellationToken cancellationToken) =>
        {
            var request = new AdminDataDeleteRecordRequest
            {
                TenantId = tenantId,
                RecordId = recordId
            };

            var result = await dataService.DeleteRecordAsync(request, cancellationToken);
            
            // Handle NotFound case with structured response
            if (!result.IsSuccess && result.StatusCode == StatusCode.NotFound)
            {
                var notFoundResponse = new AdminDataDeleteRecordResponse
                {
                    Deleted = false,
                    RecordId = recordId,
                    DeletedRecord = null
                };
                return Results.Json(notFoundResponse, statusCode: StatusCodes.Status404NotFound);
            }
            
            return result.ToHttpResult();
        })
        .Produces<AdminDataDeleteRecordResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("DeleteAdminDataRecord")
        ;
    }
}