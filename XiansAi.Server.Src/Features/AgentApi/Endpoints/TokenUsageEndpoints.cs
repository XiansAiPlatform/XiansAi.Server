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

        group.MapGet("/status", async (
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITokenUsageService usageService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(tenantContext.TenantId))
            {
                return Results.BadRequest("Tenant context is not available.");
            }

            var userId = ResolveUserId(tenantContext);
            var status = await usageService.CheckAsync(tenantContext.TenantId, userId, cancellationToken);

            var response = new AgentUsageStatusResponse
            {
                TenantId = tenantContext.TenantId,
                UserId = userId,
                Enabled = status.Enabled,
                MaxTokens = status.MaxTokens,
                TokensUsed = status.TokensUsed,
                TokensRemaining = status.TokensRemaining,
                WindowSeconds = status.WindowSeconds,
                WindowStart = status.WindowStart,
                WindowEndsAt = status.WindowEndsAt,
                IsExceeded = status.IsExceeded
            };

            return Results.Ok(response);
        })
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get current token usage status";
            operation.Description = "Returns the remaining token quota for the certificate's tenant/user context.";
            return operation;
        });

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

            if (request.PromptTokens < 0 || request.CompletionTokens < 0)
            {
                return Results.BadRequest("Token counts cannot be negative.");
            }

            if (request.PromptTokens == 0 && request.CompletionTokens == 0)
            {
                return Results.BadRequest("At least one token count must be greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(tenantContext.TenantId))
            {
                return Results.BadRequest("Tenant context is not available.");
            }

            var userId = request.UserId ?? ResolveUserId(tenantContext);

            var record = new TokenUsageRecord(
                tenantContext.TenantId,
                userId,
                request.Model,
                request.PromptTokens,
                request.CompletionTokens,
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

    private static string ResolveUserId(ITenantContext tenantContext)
    {
        if (!string.IsNullOrWhiteSpace(tenantContext.LoggedInUser))
        {
            return tenantContext.LoggedInUser;
        }

        // Fallback to tenant ID if no user context is available (e.g., system certificate)
        return tenantContext.TenantId;
    }

    private sealed class AgentUsageStatusResponse
    {
        public required string TenantId { get; init; }
        public required string UserId { get; init; }
        public bool Enabled { get; init; }
        public long MaxTokens { get; init; }
        public long TokensUsed { get; init; }
        public long TokensRemaining { get; init; }
        public int WindowSeconds { get; init; }
        public DateTime WindowStart { get; init; }
        public DateTime WindowEndsAt { get; init; }
        public bool IsExceeded { get; init; }
    }

    private sealed class AgentUsageReportRequest
    {
        public string? UserId { get; set; }
        public string? Model { get; set; }
        public long PromptTokens { get; set; }
        public long CompletionTokens { get; set; }
        public string? WorkflowId { get; set; }
        public string? RequestId { get; set; }
        public string? Source { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }
}

