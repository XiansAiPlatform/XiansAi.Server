using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Utils.Services;
using Features.AdminApi.Auth;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for document data access and analytics.
/// Provides schema discovery, retrieval, create, update, and delete for admin dashboards.
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
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>()
            .EnforceCapabilities();

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
        .RequireCapability(CapabilityActions.TenantDataSchema);

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
        .RequireCapability(CapabilityActions.TenantDataList);

        dataGroup.MapPost("", async (
            string tenantId,
            [FromBody] AdminDataCreateRequest request,
            [FromServices] IAdminDataService dataService,
            CancellationToken cancellationToken) =>
        {
            var result = await dataService.CreateDataAsync(tenantId, request, cancellationToken);
            return result.ToHttpResult();
        })
        .Produces<AdminDataItemResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("CreateAdminData")
        .WithSummary("Create a data record")
        .WithDescription("Creates a new document for the given agent. Tenant is taken from the route. Duplicate type+key in the tenant returns 409.")
        ;

        dataGroup.MapGet("/{recordId}", async (
            string tenantId,
            string recordId,
            [FromServices] IAdminDataService dataService,
            CancellationToken cancellationToken) =>
        {
            var result = await dataService.GetRecordAsync(tenantId, recordId, cancellationToken);
            return result.ToHttpResult();
        })
        .Produces<AdminDataItemResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminDataRecord")
        .WithSummary("Get a data record by ID")
        .WithDescription("Retrieves a single data record. Records from another tenant are returned as 404.")
        ;

        dataGroup.MapPut("/{recordId}", async (
            string tenantId,
            string recordId,
            [FromBody] AdminDataUpdateRequest request,
            [FromServices] IAdminDataService dataService,
            CancellationToken cancellationToken) =>
        {
            var result = await dataService.UpdateDataAsync(tenantId, recordId, request, cancellationToken);
            return result.ToHttpResult();
        })
        .Produces<AdminDataItemResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("UpdateAdminData")
        .WithSummary("Update a data record")
        .WithDescription("Partially updates an existing data record. Id, tenant, and agent cannot be changed. Missing or cross-tenant records return 404.")
        ;

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
        .RequireCapability(CapabilityActions.TenantDataDelete);

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
        .RequireCapability(CapabilityActions.TenantDataDeleteRecord);

        // Delete all documents (every type) for a given agent activation.
        // Note: "activationId" here is the activation's name, not the AgentActivation record's id.
        dataGroup.MapDelete("/agents/{agentName}/activation/{activationId}", async (
            string tenantId,
            string agentName,
            string activationId,
            [FromServices] IAdminDataService dataService) =>
        {
            var result = await dataService.DeleteDocumentsByActivationAsync(tenantId, agentName, activationId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new { message = $"Deleted {result.Data} document(s)", deletedCount = result.Data });
        })
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("DeleteDocumentsByActivation")
        .RequireCapability(CapabilityActions.TenantDataDeleteByActivation);
    }
}
