using System.Text.Json;
using Features.AdminApi.Auth;
using ResultHandler = Features.Shared.Configuration.AuthorizationMiddlewareResultHandler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.UnitTests.Shared.Configuration;

/// <summary>
/// Covers how <see cref="ResultHandler"/> answers a challenged Admin API
/// request: 401 for a credential failure, but 404 or 400 when the authentication handler recorded
/// that a SysAdmin named a missing or malformed tenant.
/// </summary>
public class AuthorizationMiddlewareResultHandlerTests
{
    private static readonly AuthorizationPolicy Policy =
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    private static async Task<(int StatusCode, JsonElement Body)> ChallengeAsync(
        string reason, AdminAuthFailure? failure)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/admin/tenants/ghost-tenant";
        context.Response.Body = new MemoryStream();
        context.Items[AdminEndpointAuthenticationHandler.FailureReasonItemKey] = reason;
        if (failure != null)
        {
            context.Items[AdminEndpointAuthenticationHandler.FailureKindItemKey] = failure.Value;
        }

        var handler = new ResultHandler(
            NullLogger<ResultHandler>.Instance);
        await handler.HandleAsync(
            _ => throw new InvalidOperationException("A challenged request must not reach the endpoint"),
            context, Policy, PolicyAuthorizationResult.Challenge());

        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, json.RootElement.Clone());
    }

    [Fact]
    public async Task TenantNotFound_IsAnswered404_WithTheNotFoundBody()
    {
        var (status, body) = await ChallengeAsync("Tenant 'ghost-tenant' was not found", AdminAuthFailure.TenantNotFound);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal("Not found", body.GetProperty("error").GetString());
        Assert.Equal("Tenant 'ghost-tenant' was not found", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task InvalidTenantId_IsAnswered400()
    {
        var (status, body) = await ChallengeAsync("Invalid tenant ID format", AdminAuthFailure.InvalidTenantId);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("Bad request", body.GetProperty("error").GetString());
        Assert.Equal("Invalid tenant ID format", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task CredentialFailure_IsStillAnswered401_WithTheRecordedReason()
    {
        var (status, body) = await ChallengeAsync("Invalid API key", failure: null);

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.Equal("Unauthorized", body.GetProperty("error").GetString());
        Assert.Equal("Invalid API key", body.GetProperty("message").GetString());
    }
}
