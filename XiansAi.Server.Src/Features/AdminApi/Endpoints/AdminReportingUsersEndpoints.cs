using Features.AdminApi.Auth;
using Features.AdminApi.Configuration;
using Features.AdminApi.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Repositories;
using Shared.Auth;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;
using Shared.Data.Models;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for managing reporting users for agent instances.
/// </summary>
public static class AdminReportingUsersEndpoints
{
    /// <summary>
    /// Request model for adding a reporting target.
    /// </summary>
    public class AddReportingTargetRequest
    {
        [Required]
        [StringLength(200, MinimumLength = 1)]
        public required string Id { get; set; }  // Target identifier (userId, groupId, email, etc.)
        
        /// <summary>
        /// Optional attributes dictionary for additional metadata (type, email, name, etc.).
        /// Both keys and values are strings for simplicity.
        /// </summary>
        public Dictionary<string, string>? Attributes { get; set; }
    }

    /// <summary>
    /// Maps all AdminApi reporting users endpoints.
    /// </summary>
    public static void MapAdminReportingUsersEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminReportingGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/agents/{agentId}/reporting-targets")
            .WithTags("AdminAPI - Agent Reporting Targets")
            .RequiresAdminApiAuth();

        // Get Reporting Users
        adminReportingGroup.MapGet("", async (
            string tenantId,
            string agentId,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Parse agent ID
                var (parsedTenant, agentName) = AgentIdParser.Parse(agentId, tenantId);

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Get agent
                var agent = await agentRepository.GetByNameAsync(
                    agentName, 
                    parsedTenant, 
                    tenantContext.LoggedInUser, 
                    tenantContext.UserRoles.ToArray());

                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent '{agentId}' not found" });
                }

                var reportingTargets = agent.GetReportingTargets();
                return Results.Ok(new
                {
                    agentId = AgentIdParser.Format(parsedTenant, agentName),
                    reportingTargets = reportingTargets.Select(rt => new
                    {
                        id = rt.Id,
                        attributes = rt.Attributes ?? new Dictionary<string, string>(),
                        addedAt = rt.AddedAt,
                        addedBy = rt.AddedBy
                    }).ToList(),
                    totalCount = reportingTargets.Count
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error retrieving reporting users: {ex.Message}");
            }
        })
        .WithName("GetReportingTargets")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Reporting Targets",
            Description = "Get list of reporting targets (users, groups, etc.) with their attributes for an agent instance."
        });

        // Add Reporting Target
        adminReportingGroup.MapPost("", async (
            string tenantId,
            string agentId,
            [FromBody] AddReportingTargetRequest request,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Parse agent ID
                var (parsedTenant, agentName) = AgentIdParser.Parse(agentId, tenantId);

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(new { error = "Forbidden", message = "Access denied" }, statusCode: 403);
                }

                // Get agent
                var agent = await agentRepository.GetByNameInternalAsync(agentName, parsedTenant);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent '{agentId}' not found" });
                }

                // Check permissions (must be owner or admin)
                if (!agent.OwnerAccess.Contains(tenantContext.LoggedInUser) &&
                    !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) &&
                    !tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
                {
                    return Results.Json(new { error = "Forbidden", message = "Access denied" }, statusCode: 403);
                }

                // Create reporting target
                // Attributes is now Dictionary<string, string> so no JsonElement conversion needed
                var reportingTarget = new ReportingTo
                {
                    Id = request.Id,
                    Attributes = request.Attributes ?? new Dictionary<string, string>(),
                    AddedAt = DateTime.UtcNow,
                    AddedBy = tenantContext.LoggedInUser
                };

                // Add reporting target
                var added = await agentRepository.AddReportingTargetAsync(agent.Id, reportingTarget);
                if (added)
                {
                    return Results.Created($"{AdminApiConfiguration.GetBasePath()}/tenants/{tenantId}/agents/{agentId}/reporting-targets/{request.Id}", 
                        new { 
                            message = "Reporting target added successfully", 
                            id = reportingTarget.Id,
                            attributes = reportingTarget.Attributes,
                            addedAt = reportingTarget.AddedAt,
                            addedBy = reportingTarget.AddedBy
                        });
                }

                return Results.Conflict(new { error = "Reporting target already exists or failed to add" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error adding reporting target: {ex.Message}");
            }
        })
        .WithName("AddReportingTarget")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Add Reporting Target",
            Description = "Add a reporting target (user, group, etc.) with optional attributes to an agent instance."
        });

        // Remove Reporting Target
        adminReportingGroup.MapDelete("/{targetId}", async (
            string tenantId,
            string agentId,
            string targetId,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Parse agent ID
                var (parsedTenant, agentName) = AgentIdParser.Parse(agentId, tenantId);

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(new { error = "Forbidden", message = "Access denied" }, statusCode: 403);
                }

                // Get agent
                var agent = await agentRepository.GetByNameInternalAsync(agentName, parsedTenant);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent '{agentId}' not found" });
                }

                // Check permissions (must be owner or admin)
                if (!agent.OwnerAccess.Contains(tenantContext.LoggedInUser) &&
                    !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) &&
                    !tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
                {
                    return Results.Json(new { error = "Forbidden", message = "Access denied" }, statusCode: 403);
                }

                // Remove reporting target
                var removed = await agentRepository.RemoveReportingTargetAsync(agent.Id, targetId);
                if (removed)
                {
                    return Results.Ok(new { message = "Reporting target removed successfully", targetId = targetId });
                }

                return Results.NotFound(new { error = "Reporting target not found" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error removing reporting target: {ex.Message}");
            }
        })
        .WithName("RemoveReportingTarget")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Remove Reporting Target",
            Description = "Remove a reporting target (user, group, etc.) by ID from an agent instance."
        });
    }
}

