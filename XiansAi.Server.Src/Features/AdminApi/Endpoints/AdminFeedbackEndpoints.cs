using Features.AdminApi.Auth;
using Features.AdminApi.Constants;
using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for submitting feedback on agent messages.
///
/// These endpoints live under <c>/api/v{version}/admin/tenants/{tenantId}/feedback</c>. The tenant
/// is resolved authoritatively by <see cref="AdminRoleTenantResolver"/>: a TenantAdmin is locked to
/// their own tenant, while a SysAdmin may target any existing tenant via the route. This lets
/// SysAdmins publish feedback regardless of which tenant they belong to. The
/// <see cref="TenantRouteScopeFilter"/> guarantees the route tenant matches the resolved context
/// tenant, and <see cref="IFeedbackService"/> scopes the feedback to that resolved tenant.
/// </summary>
public static class AdminFeedbackEndpoints
{
    public static void MapAdminFeedbackEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var feedbackGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/feedback")
            .WithTags("AdminAPI - Feedback")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

        feedbackGroup.MapPost("", async (
            string tenantId,
            [FromBody] SubmitMessageFeedbackRequest request,
            [FromServices] IFeedbackService feedbackService) =>
        {
            var result = await feedbackService.SubmitFeedbackAsync(request);
            if (result.IsSuccess && result.Data != null)
            {
                var location = AdminApiConstants.BuildVersionedPath(
                    $"tenants/{Uri.EscapeDataString(tenantId)}/feedback/{Uri.EscapeDataString(result.Data)}");
                return Results.Created(location, new { id = result.Data });
            }

            return result.ToHttpResult();
        })
        .WithName("AdminSubmitMessageFeedback")
        .WithSummary("Submit feedback for an agent message")
        .WithDescription(
            "Submits a 1–5 star rating for an outgoing (agent) message in the specified tenant. " +
            "SysAdmins may publish feedback for any tenant; TenantAdmins are restricted to their own tenant. " +
            "When rating is below 4, reasonCategory is required; if reason is Other, comment is required.");

        // Aggregated feedback statistics for dashboards (counts per rating, per reason category, average).
        feedbackGroup.MapGet("/stats", async (
            string tenantId,
            [FromServices] IFeedbackQueryService feedbackQueryService,
            [FromQuery] int? rating = null,
            [FromQuery] string? agentName = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null) =>
        {
            var filter = new FeedbackFilter
            {
                Rating = rating,
                AgentName = agentName,
                StartDate = startDate,
                EndDate = endDate
            };

            var result = await feedbackQueryService.GetStatsAsync(tenantId, filter);
            return result.ToHttpResult();
        })
        .Produces<FeedbackStatsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("AdminGetFeedbackStats")
        .WithSummary("Get feedback statistics")
        .WithDescription(
            "Returns aggregated feedback statistics for the tenant: total count, average rating, " +
            "counts per star rating (1-5), and counts per reason category. Optionally filter by rating, " +
            "agentName, and a submittedAt date range.");

        // Paginated, filterable list of feedback (newest first).
        feedbackGroup.MapGet("", async (
            string tenantId,
            [FromServices] IFeedbackQueryService feedbackQueryService,
            [FromQuery] int? rating = null,
            [FromQuery] string? agentName = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var filter = new FeedbackFilter
            {
                Rating = rating,
                AgentName = agentName,
                StartDate = startDate,
                EndDate = endDate
            };

            var result = await feedbackQueryService.ListAsync(tenantId, filter, page, pageSize);
            return result.ToHttpResult();
        })
        .Produces<FeedbackListResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("AdminListFeedback")
        .WithSummary("List feedback")
        .WithDescription(
            "Returns a paginated list of feedback for the tenant, newest first. Filter by rating " +
            "(1-5), agentName, and a submittedAt date range. Page size is limited to 100.");

        // Single feedback entry with surrounding thread messages for context.
        feedbackGroup.MapGet("/{feedbackId}", async (
            string tenantId,
            string feedbackId,
            [FromServices] IFeedbackQueryService feedbackQueryService,
            [FromQuery] int contextBefore = 5,
            [FromQuery] int contextAfter = 5,
            [FromQuery] bool chatOnly = false) =>
        {
            var result = await feedbackQueryService.GetDetailAsync(
                tenantId, feedbackId, contextBefore, contextAfter, chatOnly);
            return result.ToHttpResult();
        })
        .Produces<FeedbackDetailResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminGetFeedbackDetail")
        .WithSummary("Get feedback detail with thread context")
        .WithDescription(
            "Returns a single feedback entry together with the surrounding messages from its thread " +
            "(up to contextBefore messages before and contextAfter messages after the rated message, " +
            "each capped at 50). Set chatOnly=true to include only chat messages in the surrounding context. " +
            "The rated message is always included and is identified by ratedMessageId.");
    }
}
