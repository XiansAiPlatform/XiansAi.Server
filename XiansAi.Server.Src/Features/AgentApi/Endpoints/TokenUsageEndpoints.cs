using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Auth;
using Shared.Services;

namespace Features.AgentApi.Endpoints;

public static class TokenUsageEndpoints
{
    public static void MapTokenUsageEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/agent/usage")
            .WithTags("AgentAPI - Usage")
            .RequiresCertificate();

        group.MapPost("/report", async (
            [FromBody] AgentUsageReportRequest request,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITokenUsageService usageService,
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

            var record = new TokenUsageRecord(
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
                request.Metadata);

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

    private sealed class AgentUsageReportRequest
    {
        public string? TenantId { get; set; }
        public string? UserId { get; set; }
        public string? Model { get; set; }
        public long PromptTokens { get; set; }
        public long CompletionTokens { get; set; }
        public long TotalTokens { get; set; }
        public long MessageCount { get; set; }
        public string? WorkflowId { get; set; }
        public string? RequestId { get; set; }
        public string? Source { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }
}

