using Features.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Models;
using Features.WebApi.Services;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

/// <summary>
/// Provides extension methods for registering AI Copilot code generation API endpoints.
/// </summary>
public static class CopilotEndpoints
{
    /// <summary>
    /// Maps all AI Copilot code generation endpoints to the application's request pipeline.
    /// </summary>
    /// <param name="app">The web application to add endpoints to.</param>
    /// <returns>The web application with Copilot code endpoints configured.</returns>
    public static void MapCopilotEndpoints(this WebApplication app)
    {
        // Map Copilot code endpoints with common attributes
        var copilotGroup = app.MapGroup("/api/client/copilot")
            .WithTags("AI Copilot Code Generation")
            .RequiresValidTenant();

        // Generate agent code endpoint
        copilotGroup.MapPost("/generate", GenerateCode)
            .WithName("Generate Code with Copilot")
            .WithOpenApi(operation =>
            {
                operation.Summary = "Generate C# Agent Code with AI Copilot";
                operation.Description = "Generates C# agent worker code based on user description using AI Copilot";
                return operation;
            });

        // Refine existing code endpoint
        copilotGroup.MapPost("/refine", RefineCode)
            .WithName("Refine Code with Copilot")
            .WithOpenApi(operation =>
            {
                operation.Summary = "Refine Agent Code with AI Copilot";
                operation.Description = "Refines existing agent code based on user feedback using AI Copilot";
                return operation;
            });
    }

    /// <summary>
    /// Generates C# agent code using AI Copilot based on user description
    /// </summary>
    /// <param name="request">Code generation request</param>
    /// <param name="copilotService">AI Copilot code generation service</param>
    /// <returns>Copilot-generated agent code response</returns>
    private static async Task<IResult> GenerateCode(
        [FromBody] CopilotCodeRequest request,
        [FromServices] ICopilotService copilotService)
    {
        try
        {
            var result = await copilotService.GenerateAgentCodeAsync(request);

            return result.StatusCode switch
            {
                StatusCode.Ok => Results.Ok(result.Data),
                StatusCode.BadRequest => Results.BadRequest(new { error = result.ErrorMessage }),
                StatusCode.InternalServerError => Results.Problem(
                    detail: result.ErrorMessage,
                    statusCode: 500,
                    title: "Internal Server Error"),
                _ => Results.Problem(
                    detail: "An unexpected error occurred",
                    statusCode: 500,
                    title: "Internal Server Error")
            };
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: $"Copilot encountered an error while generating agent code: {ex.Message}",
                statusCode: 500,
                title: "Internal Server Error");
        }
    }

    /// <summary>
    /// Refines existing agent code using AI Copilot based on user feedback
    /// </summary>
    /// <param name="request">Code refinement request</param>
    /// <param name="copilotService">AI Copilot code generation service</param>
    /// <returns>Copilot-refined agent code response</returns>
    private static async Task<IResult> RefineCode(
        [FromBody] RefineCodeRequest request,
        [FromServices] ICopilotService copilotService)
    {
        try
        {
            var result = await copilotService.RefineCodeAsync(
                request.CurrentCode, 
                request.RefinementRequest, 
                request.ConversationHistory);

            return result.StatusCode switch
            {
                StatusCode.Ok => Results.Ok(result.Data),
                StatusCode.BadRequest => Results.BadRequest(new { error = result.ErrorMessage }),
                StatusCode.InternalServerError => Results.Problem(
                    detail: result.ErrorMessage,
                    statusCode: 500,
                    title: "Internal Server Error"),
                _ => Results.Problem(
                    detail: "An unexpected error occurred",
                    statusCode: 500,
                    title: "Internal Server Error")
            };
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: $"Copilot encountered an error while refining code: {ex.Message}",
                statusCode: 500,
                title: "Internal Server Error");
        }
    }
} 