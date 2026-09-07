using System.Text.Json;
using Features.AdminApi.Auth;
using Shared.Configuration;
using Shared.Services;

namespace Features.AdminApi.Services;

/// <summary>
/// Provisions the "admin-console" pseudo-tenant's OIDC config from environment/appsettings
/// (AdminConsoleOidcSettings) instead of requiring a manual API call, mirroring how
/// Features/WebApi/Scripts/SeedData.cs seeds other startup data. Runs once at startup, called
/// from Program.cs alongside SeedData.SeedDefaultDataAsync; safe to call on every restart since
/// TenantOidcConfigService.UpsertAsync is a plain replace, not additive.
///
/// Deliberately writes through the same ITenantOidcConfigService/TenantOidcConfigService a real
/// tenant's config goes through (Mongo-backed, the one already live for UserApi) rather than a
/// second in-memory ITenantOidcConfigService implementation: registering a second implementation
/// of that interface risks shadowing the real one for every other caller, since this codebase
/// already registers it more than once across features and DI resolves whichever registration
/// ran last. Writing a normal record through the existing service avoids that risk entirely.
/// </summary>
public static class AdminConsoleOidcSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider, ILogger logger)
    {
        using var scope = serviceProvider.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var settings = configuration.GetSection(AdminConsoleOidcSettings.SectionName).Get<AdminConsoleOidcSettings>()
            ?? new AdminConsoleOidcSettings();

        if (!settings.Enabled)
        {
            logger.LogInformation("Admin console OIDC seeding disabled ({Section}:Enabled=false).",
                AdminConsoleOidcSettings.SectionName);
            return;
        }

        var rules = BuildRules(settings);
        if (rules == null)
        {
            logger.LogInformation(
                "No admin-console OIDC providers configured under {Section}:Providers:*. AdminApi " +
                "keeps working on the API key alone; any forwarded X-User-Token will fail closed " +
                "until at least one provider is configured.",
                AdminConsoleOidcSettings.SectionName);
            return;
        }

        var configService = scope.ServiceProvider.GetRequiredService<ITenantOidcConfigService>();
        var json = JsonSerializer.Serialize(rules);
        var result = await configService.UpsertAsync(
            AdminActingUserResolver.AdminConsolePseudoTenant, json, "system-startup-seed");

        if (!result.IsSuccess)
        {
            logger.LogError("Failed to seed admin-console OIDC config: {Error}", result.ErrorMessage);
        }
        else
        {
            logger.LogInformation("Seeded admin-console OIDC config with providers: {Providers}",
                string.Join(", ", rules.Providers!.Keys));
        }
    }

    /// <summary>
    /// Pure so it's unit-testable without DI/Mongo. Skips any provider entry missing Authority or
    /// ClientId rather than failing the whole seed - a typo in one provider's env vars shouldn't
    /// take every other configured provider down with it.
    /// </summary>
    public static TenantOidcRules? BuildRules(AdminConsoleOidcSettings settings)
    {
        if (settings.Providers == null || settings.Providers.Count == 0)
        {
            return null;
        }

        var providers = new Dictionary<string, OidcProviderRule>();
        foreach (var (name, provider) in settings.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Authority) || string.IsNullOrWhiteSpace(provider.ClientId))
            {
                continue;
            }

            providers[name] = new OidcProviderRule
            {
                Authority = provider.Authority,
                Issuer = string.IsNullOrWhiteSpace(provider.Issuer) ? provider.Authority : provider.Issuer,
                ExpectedAudience = new List<string> { provider.ClientId },
                // Left unset unless explicitly configured: DynamicOidcValidator only checks a
                // scope/scp claim when Scope is non-empty (OidcTokenInspector.DescribeMissingScope).
                // The token forwarded here is an ID token, and scope is not a standard ID token
                // claim (it's a token-response/access-token field) - defaulting this to a non-empty
                // value would fail every real ID token unconditionally, not verify anything.
                Scope = string.IsNullOrWhiteSpace(provider.Scope) ? null : provider.Scope,
                AcceptedAlgorithms = new List<string> { "RS256" }
            };
        }

        if (providers.Count == 0)
        {
            return null;
        }

        return new TenantOidcRules
        {
            TenantId = AdminActingUserResolver.AdminConsolePseudoTenant,
            AllowedProviders = providers.Keys.ToList(),
            Providers = providers,
            Notes = "Identity provider(s) for AdminApi's own callers (agent-studio and any future " +
                "admin client) - not a real tenant. Auto-provisioned at startup from " +
                AdminConsoleOidcSettings.SectionName + ":Providers:* configuration."
        };
    }
}
