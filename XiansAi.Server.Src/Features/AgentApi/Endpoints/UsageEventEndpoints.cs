using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Auth;
using Shared.Data.Models.Usage;
using Shared.Services;

namespace Features.AgentApi.Endpoints;

public static class UsageEventEndpoints
{
    public static void MapUsageEventEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/agent/usage")
            .WithTags("AgentAPI - Usage")
            .RequiresCertificate();

        group.MapPost("/report", async (
            [FromBody] UsageReportRequest request,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUsageEventService usageService,
            CancellationToken cancellationToken) =>
        {
            if (request == null)
            {
                return Results.BadRequest("Request payload is required.");
            }

            if (request.PromptTokens < 0 || request.CompletionTokens < 0 || request.TotalTokens < 0)
            {
                return Results.BadRequest("Token counts cannot be negative.");
            }

            if (request.PromptTokens == 0 && request.CompletionTokens == 0 && request.TotalTokens == 0)
            {
                return Results.BadRequest("At least one token count must be greater than zero.");
            }

            // Use TenantId from request if provided, otherwise from certificate context
            var tenantId = request.TenantId ?? tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return Results.BadRequest("Tenant context is not available.");
            }

            var userId = request.UserId ?? tenantContext.LoggedInUser ?? tenantContext.TenantId;

            var record = new UsageEventRecord(
                tenantId,
                userId,
                request.Model,
                request.PromptTokens,
                request.CompletionTokens,
                request.TotalTokens,
                request.MessageCount,
                request.WorkflowId,
                request.RequestId,
                request.Source,
                request.Metadata,
                request.ResponseTimeMs);

            await usageService.RecordAsync(record, cancellationToken);

            return Results.Accepted();
        })
        .WithOpenApi(operation =>
        {
            operation.Summary = "Report token usage";
            operation.Description = "Reports prompt/completion token usage for the current tenant/user context.";
            return operation;
        });
    }
}

