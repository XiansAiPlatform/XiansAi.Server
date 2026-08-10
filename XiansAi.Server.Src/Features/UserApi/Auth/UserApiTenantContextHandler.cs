using Microsoft.AspNetCore.Authorization;
using Shared.Auth;
using Shared.Utils;
using System.Security.Claims;

namespace Features.UserApi.Auth;

/// <summary>
/// Restores the tenant context from an already-authenticated UserApi principal.
///
/// Authentication populates the tenant context too, but that happens on a different scope-crossing
/// path than the one endpoints run on, so the values are re-established here from the claims the
/// authentication handler minted. Claims are the source of truth: they are what authentication
/// actually decided, so nothing here re-derives or widens them.
///
/// The HTTP and WebSocket policies need identical behaviour, so they share this base and differ
/// only in which requirement they satisfy.
/// </summary>
public abstract class UserApiTenantContextHandler<TRequirement> : AuthorizationHandler<TRequirement>
    where TRequirement : IAuthorizationRequirement
{
    private readonly ITenantContext _tenantContext;
    private readonly ILogger _logger;

    protected UserApiTenantContextHandler(ITenantContext tenantContext, ILogger logger)
    {
        _tenantContext = tenantContext;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TRequirement requirement)
    {
        // Authorization still runs when authentication failed, and the principal is then simply
        // anonymous. That is not an anomaly worth reporting — authentication has already logged why
        // it refused — so fail quietly and leave that log line as the single explanation.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        var loggedInUser = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(loggedInUser))
        {
            _logger.LogWarning("Authenticated principal carries no user id");
            context.Fail();
            return Task.CompletedTask;
        }

        var tenantId = context.User.FindFirst(UserApiClaimTypes.TenantId)?.Value;
        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("Authenticated principal carries no tenant id");
            context.Fail();
            return Task.CompletedTask;
        }

        _tenantContext.LoggedInUser = loggedInUser;
        _tenantContext.UserType = UserApiClaimTypes.ReadUserType(context.User);
        _tenantContext.TenantId = tenantId;

        // Narrowed to the tenant this request is for, even when the caller is an approved member of
        // several. Services authorize cross-tenant reads off this list, so a UserApi request only
        // ever reaches the one tenant it named.
        _tenantContext.AuthorizedTenantIds = new[] { tenantId };

        // Absent for credentials where the participant is the logged-in user, in which case the
        // tenant context already falls back to it.
        var participantId = context.User.FindFirst(UserApiClaimTypes.ParticipantId)?.Value;
        if (!string.IsNullOrEmpty(participantId))
        {
            _tenantContext.ParticipantId = participantId;
        }

        _tenantContext.Email = context.User.FindFirst(UserApiClaimTypes.Email)?.Value;
        _tenantContext.ProviderSubject = context.User.FindFirst(UserApiClaimTypes.ProviderSubject)?.Value;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Restored tenant context for user {UserId} on tenant {TenantId}",
                LogSanitizer.Sanitize(loggedInUser), LogSanitizer.Sanitize(tenantId));
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
