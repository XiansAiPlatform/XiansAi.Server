using Features.WebApi.Auth.Providers;
using Microsoft.AspNetCore.Authorization;
using Shared.Auth;
using XiansAi.Server.Features.WebApi.Services;

namespace Features.WebApi.Auth;

public class ValidTokenRequirement : BaseAuthRequirement
{    
    public ValidTokenRequirement(IConfiguration configuration) : base(configuration)
    {
    }
}

public class TokenClientHandler : BaseAuthHandler<ValidTokenRequirement>
{
    private readonly IUserTenantCacheService _userTenantCacheService;

    public TokenClientHandler(
        ILogger<TokenClientHandler> logger, 
        ITenantContext tenantContext,
        IUserTenantCacheService userTenantCacheService,
        IAuthProviderFactory authProviderFactory)
        : base(logger, tenantContext, authProviderFactory)
    {
        _userTenantCacheService = userTenantCacheService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ValidTokenRequirement requirement)
    {
        var (success, loggedInUser) = await ValidateToken(context);
        
        if (!success)
        {
            context.Fail();
            return;
        }

        // get authorized tenant IDs from cache or DB collection
        var authorizedTenantIds = await _userTenantCacheService.GetUserTenantAsync(loggedInUser!);

        // set tenant context and succeed
        _tenantContext.AuthorizedTenantIds = authorizedTenantIds ?? new List<string>();
        _tenantContext.LoggedInUser = loggedInUser ?? throw new InvalidOperationException("Logged in user not found");
        
        context.Succeed(requirement);
    }
}