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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Available Data Schema",
            Description = @"Get available data types and filters for the specified agent within a date range.

Query Parameters (Required):
- startDate: Start of date range (ISO 8601 format)
- endDate: End of date range (ISO 8601 format)
- agentName: Filter by specific agent name
- activationName: Filter by specific activation name (optional)

Returns:
- period: The date range queried
- filters: The agent and activation filters applied
- types: Available data types for the specified filters (e.g., [""Companies"", ""Mails Sent""])

Examples:
- GET /api/v1/admin/tenants/{tenantId}/data/schema?startDate=2026-01-01T00:00:00Z&endDate=2026-01-31T23:59:59Z&agentName=CustomerSupportAgent
- GET /api/v1/admin/tenants/{tenantId}/data/schema?startDate=2026-01-01T00:00:00Z&endDate=2026-01-31T23:59:59Z&agentName=CustomerSupportAgent&activationName=email-responder

Use Cases:
- Discover what data types are available for an agent
- Build dynamic UI controls for data type selection
- Validate data type parameters before calling the data endpoint

Note: The date range filter helps optimize the query and ensures you only see data types that have data in the specified period."
        });

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Paginated Data",
            Description = @"Get paginated document data for the specified filters within a date range.

Query Parameters (Required):
- startDate: Start of date range (ISO 8601 format)
- endDate: End of date range (ISO 8601 format)
- agentName: Filter by specific agent name
- dataType: The specific data type to retrieve (use schema endpoint to discover available types)

Query Parameters (Optional):
- activationName: Filter by specific activation name
- skip: Number of records to skip for pagination (default: 0)
- limit: Maximum number of records to return (default: 100, max: 1000)

Returns:
- data: Array of document data items with id, key, participantId, content, metadata, and timestamps
- total: Total number of records matching the filters (for pagination)
- skip: The skip value used (for pagination)
- limit: The limit value used (for pagination)

Response Format:
Each data item contains:
- id: Unique document identifier
- key: Document key/identifier within the agent
- participantId: ID of the participant/user associated with the document
- content: The actual document content (JSON)
- metadata: Additional metadata associated with the document
- createdAt: When the document was created
- updatedAt: When the document was last updated (if applicable)
- expiresAt: When the document expires (if applicable)

Examples:
- GET /api/v1/admin/tenants/{tenantId}/data?startDate=2026-01-01T00:00:00Z&endDate=2026-01-31T23:59:59Z&agentName=CustomerSupportAgent&dataType=Companies
- GET /api/v1/admin/tenants/{tenantId}/data?startDate=2026-01-01T00:00:00Z&endDate=2026-01-31T23:59:59Z&agentName=CustomerSupportAgent&dataType=Companies&activationName=email-responder&skip=50&limit=50

Use Cases:
- Build data tables and grids for admin dashboards
- Export data for analysis
- Monitor and debug agent data processing
- Investigate specific data processing issues

Note: Results are sorted by creation date (newest first) for consistent pagination."
        });

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete All Data Records of a Specific Type",
            Description = @"Delete all data records of a specific type within the specified filters and date range.

Query Parameters (Required):
- startDate: Start of date range (ISO 8601 format)
- endDate: End of date range (ISO 8601 format)
- agentName: Filter by specific agent name
- dataType: The specific data type to delete (use schema endpoint to discover available types)

Query Parameters (Optional):
- activationName: Filter by specific activation name

Returns:
- deletedCount: Number of records that were deleted
- period: The date range used for deletion
- filters: The agent and activation filters applied
- dataType: The data type that was deleted

Response Format:
- deletedCount: Integer count of deleted records
- period: { startDate, endDate } - the deletion range
- filters: { agentName, activationName } - the filters applied
- dataType: The specific data type that was deleted

Examples:
- DELETE /api/v1/admin/tenants/{tenantId}/data?startDate=2026-01-01T00:00:00Z&endDate=2026-01-31T23:59:59Z&agentName=CustomerSupportAgent&dataType=Companies
- DELETE /api/v1/admin/tenants/{tenantId}/data?startDate=2026-01-01T00:00:00Z&endDate=2026-01-31T23:59:59Z&agentName=CustomerSupportAgent&dataType=Companies&activationName=email-responder

Use Cases:
- Clean up test data from admin dashboards
- Remove outdated or incorrect data processing results
- Reset agent data for a specific type during development
- Bulk cleanup of agent data based on date ranges

IMPORTANT WARNINGS:
- This operation is irreversible and will permanently delete data
- Use with caution as it affects all matching records
- Consider testing with narrow date ranges first
- Ensure you have proper backups before bulk deletions
- The operation respects tenant isolation for security

Security:
- Requires admin authorization
- Tenant isolation is enforced - users can only delete data from their own tenant
- All parameters are validated before deletion"
        });

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete a Specific Data Record",
            Description = @"Delete a specific data record by its unique identifier.

Path Parameters:
- recordId (required): The unique identifier of the record to delete

Returns:
- deleted: Boolean indicating if the record was successfully deleted
- recordId: The ID of the record that was requested for deletion
- deletedRecord: The complete record data that was deleted (for confirmation)

Response Format:
- deleted: true/false - indicates successful deletion
- recordId: The requested record ID
- deletedRecord: Complete record object with all fields (id, key, content, metadata, timestamps)

Examples:
- DELETE /api/v1/admin/tenants/{tenantId}/data/697e2b40993d83f992f4ab0c

Use Cases:
- Remove specific erroneous or test records
- Clean up individual problematic data entries
- Delete records identified through admin data browsing
- Remove sensitive data that was inadvertently stored

Response Examples:

Success (200):
{
  ""deleted"": true,
  ""recordId"": ""697e2b40993d83f992f4ab0c"",
  ""deletedRecord"": {
    ""id"": ""697e2b40993d83f992f4ab0c"",
    ""key"": ""some-unique-key"",
    ""participantId"": ""user@example.com"",
    ""content"": { ... },
    ""metadata"": { ... },
    ""createdAt"": ""2026-01-31T16:18:08.790Z"",
    ""updatedAt"": null,
    ""expiresAt"": null
  }
}

Not Found (404):
{
  ""deleted"": false,
  ""recordId"": ""non-existent-id"",
  ""deletedRecord"": null
}

IMPORTANT SECURITY:
- Requires admin authorization
- Tenant isolation is strictly enforced - users can only delete records from their own tenant
- Returns 404 for records that don't exist OR belong to a different tenant (security by obscurity)
- The complete deleted record is returned for confirmation and audit purposes
- All deletion attempts are logged for security auditing

IMPORTANT WARNINGS:
- This operation is irreversible and will permanently delete the record
- The deleted record data is returned in the response for final confirmation
- Consider the security implications of returning deleted data in API responses
- Ensure proper logging and audit trails for compliance requirements"
        });
    }
}