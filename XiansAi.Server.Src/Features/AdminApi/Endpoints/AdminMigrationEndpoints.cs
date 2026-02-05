using Microsoft.AspNetCore.Mvc;
using Shared.Data;
using Shared.Data.Migrations;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for database migrations.
/// These endpoints allow running data migrations.
/// All endpoints are under /api/v{version}/admin/migrations prefix (versioned).
/// </summary>
public static class AdminMigrationEndpoints
{
    /// <summary>
    /// Maps all AdminApi migration endpoints.
    /// </summary>
    public static void MapAdminMigrationEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var migrationGroup = adminApiGroup.MapGroup("/migrations")
            .WithTags("AdminAPI - Migrations")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // Normalize emails migration
        migrationGroup.MapPost("/normalize-emails", async (
            [FromServices] IDatabaseService databaseService,
            [FromServices] ILogger<NormalizeEmailsMigration> logger) =>
        {
            try
            {
                var migration = new NormalizeEmailsMigration(databaseService, logger);
                var result = await migration.MigrateAsync();

                if (result.Success)
                {
                    return Results.Ok(new
                    {
                        message = "Email normalization completed successfully",
                        totalProcessed = result.TotalProcessed,
                        updated = result.Updated,
                        errors = result.Errors
                    });
                }
                else
                {
                    return Results.Problem(
                        detail: $"Email normalization completed with {result.Errors} errors",
                        statusCode: StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error running email normalization migration");
                return Results.Problem(
                    detail: "An error occurred while running the email normalization migration",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("NormalizeEmails")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Normalize User Emails",
            Description = "Migrates all existing user email addresses to lowercase for consistency and case-insensitive lookups."
        });
    }
}
