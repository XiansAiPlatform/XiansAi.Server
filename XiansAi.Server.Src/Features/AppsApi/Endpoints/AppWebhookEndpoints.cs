using System.Text;
using Microsoft.AspNetCore.Mvc;
using Features.AppsApi.Models;
using Features.AppsApi.Services;
using Features.AppsApi.Handlers;
using Shared.Services;
using Shared.Repositories;

namespace Features.AppsApi.Endpoints;

/// <summary>
/// Generic webhook endpoints for receiving events from external platforms.
/// Platform-specific endpoints are in their respective files (SlackWebhookEndpoints, TeamsWebhookEndpoints).
/// These endpoints are called by external platforms and do not require standard authentication.
/// Authentication is done via platform-specific signature verification.
/// </summary>
public static class AppWebhookEndpoints
{
    /// <summary>
    /// Maps all public webhook endpoints for app integrations.
    /// </summary>
    public static void MapAppWebhookEndpoints(this WebApplication app)
    {
        var appsGroup = app.MapGroup("/api/apps");

        // Map platform-specific endpoints (they have their own tags)
        appsGroup.MapSlackWebhookEndpoints();
        appsGroup.MapTeamsWebhookEndpoints();
        appsGroup.MapWebhookEndpoints();
        
        // Note: Outlook endpoints will be added here when implemented
    }
}
