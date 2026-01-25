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

            if (request.Metrics == null || request.Metrics.Count == 0)
            {
                return Results.BadRequest("At least one metric must be provided.");
            }

            // Validate metrics
            foreach (var metric in request.Metrics)
            {
                if (string.IsNullOrWhiteSpace(metric.Category))
                {
                    return Results.BadRequest("Metric category is required.");
                }

                if (string.IsNullOrWhiteSpace(metric.Type))
                {
                    return Results.BadRequest("Metric type is required.");
                }

                if (metric.Value < 0)
                {
                    return Results.BadRequest($"Metric value cannot be negative: {metric.Type}");
                }
            }

            // Use TenantId from request if provided, otherwise from certificate context
            var tenantId = request.TenantId ?? tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return Results.BadRequest("Tenant context is not available.");
            }

            var userId = request.UserId ?? tenantContext.LoggedInUser ?? tenantContext.TenantId;

            await usageService.RecordAsync(request, tenantId, userId, cancellationToken);

            return Results.Accepted();
        })
        .WithOpenApi(operation =>
        {
            operation.Summary = "Report flexible usage metrics";
            operation.Description = "Reports usage metrics using the flexible metrics array format. " +
                                  "Supports standard metrics (tokens, messages, response time) and custom metrics " +
                                  "(workflow completions, emails sent, etc.).\n\n" +
                                  "**Example Request:**\n" +
                                  "```json\n" +
                                  "{\n" +
                                  "  \"workflowId\": \"tenant:EmailAgent:SendEmail\",\n" +
                                  "  \"model\": \"gpt-4\",\n" +
                                  "  \"metrics\": [\n" +
                                  "    { \"category\": \"tokens\", \"type\": \"prompt_tokens\", \"value\": 100, \"unit\": \"tokens\" },\n" +
                                  "    { \"category\": \"tokens\", \"type\": \"completion_tokens\", \"value\": 50, \"unit\": \"tokens\" },\n" +
                                  "    { \"category\": \"tokens\", \"type\": \"total_tokens\", \"value\": 150, \"unit\": \"tokens\" },\n" +
                                  "    { \"category\": \"activity\", \"type\": \"email_sent\", \"value\": 1, \"unit\": \"count\" },\n" +
                                  "    { \"category\": \"activity\", \"type\": \"workflow_completed\", \"value\": 1, \"unit\": \"count\" }\n" +
                                  "  ]\n" +
                                  "}\n" +
                                  "```\n\n" +
                                  "**Standard Metric Categories:**\n" +
                                  "- `tokens`: Token usage (prompt_tokens, completion_tokens, total_tokens)\n" +
                                  "- `activity`: Agent activities (message_count, workflow_completed, email_sent, etc.)\n" +
                                  "- `performance`: Performance metrics (response_time_ms, processing_time_ms)\n" +
                                  "- `llm_usage`: LLM API usage (llm_calls, cache_hits, cache_misses)\n\n" +
                                  "**Custom Metrics:**\n" +
                                  "Any category/type combination can be used for tenant-specific metrics.";
            return operation;
        });
    }
}

