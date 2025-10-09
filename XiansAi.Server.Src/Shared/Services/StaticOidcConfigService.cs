using System.Text.Json;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// Static OIDC configuration service that reads from appsettings.json.
/// Used by WebAPI for single-provider authentication (unlike User API which supports multi-tenant/per-tenant configs).
/// </summary>
public class StaticOidcConfigService : ITenantOidcConfigService
{
    private readonly TenantOidcRules? _staticRules;
    private readonly ILogger<StaticOidcConfigService> _logger;

    public StaticOidcConfigService(IConfiguration configuration, ILogger<StaticOidcConfigService> logger)
    {
        _logger = logger;
        
        // Read Oidc section from appsettings.json
        var oidcSection = configuration.GetSection("Oidc");
        
        if (!oidcSection.Exists())
        {
            _logger.LogWarning("Oidc configuration section is missing in appsettings.json. WebAPI OIDC authentication will not work.");
            _staticRules = null;
            return;
        }

        try
        {
            // Extract configuration values
            var providerName = oidcSection["ProviderName"] ?? "default";
            var authority = oidcSection["Authority"];
            var issuer = oidcSection["Issuer"];
            var audience = oidcSection["Audience"];
            var scopes = oidcSection["Scopes"];
            
            // Parse optional boolean values
            var requireSignedTokens = oidcSection["RequireSignedTokens"];
            var requireHttpsMetadata = oidcSection["RequireHttpsMetadata"];
            
            // Parse accepted algorithms (comma-separated or array)
            List<string>? acceptedAlgorithms = null;
            var algorithmsValue = oidcSection["AcceptedAlgorithms"];
            if (!string.IsNullOrWhiteSpace(algorithmsValue))
            {
                acceptedAlgorithms = algorithmsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
            else
            {
                // Try to read as array from configuration
                var algsArray = oidcSection.GetSection("AcceptedAlgorithms").Get<List<string>>();
                if (algsArray != null && algsArray.Any())
                {
                    acceptedAlgorithms = algsArray;
                }
            }

            // Parse expected audience (can be single value or array)
            List<string>? expectedAudience = null;
            if (!string.IsNullOrWhiteSpace(audience))
            {
                expectedAudience = new List<string> { audience };
            }
            else
            {
                // Try to read as array
                var audienceArray = oidcSection.GetSection("Audience").Get<List<string>>();
                if (audienceArray != null && audienceArray.Any())
                {
                    expectedAudience = audienceArray;
                }
            }

            // Parse claim mappings
            Dictionary<string, object>? providerSpecificSettings = null;
            var claimMappingsSection = oidcSection.GetSection("ClaimMappings");
            if (claimMappingsSection.Exists())
            {
                providerSpecificSettings = new Dictionary<string, object>();
                
                var userIdClaim = claimMappingsSection["UserIdClaim"];
                if (!string.IsNullOrWhiteSpace(userIdClaim))
                {
                    providerSpecificSettings["userIdClaim"] = userIdClaim;
                }
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(authority))
            {
                throw new InvalidOperationException("Oidc:Authority is required in appsettings.json");
            }

            if (expectedAudience == null || !expectedAudience.Any())
            {
                throw new InvalidOperationException("Oidc:Audience is required in appsettings.json");
            }

            // Build the provider rule
            var providerRule = new OidcProviderRule
            {
                Authority = authority,
                Issuer = issuer ?? authority, // Default issuer to authority if not specified
                ExpectedAudience = expectedAudience,
                Scope = scopes,
                RequireSignedTokens = string.IsNullOrWhiteSpace(requireSignedTokens) ? true : bool.Parse(requireSignedTokens),
                AcceptedAlgorithms = acceptedAlgorithms,
                RequireHttpsMetadata = string.IsNullOrWhiteSpace(requireHttpsMetadata) ? true : bool.Parse(requireHttpsMetadata),
                ProviderSpecificSettings = providerSpecificSettings
            };

            // Build the tenant rules (for "webapi" pseudo-tenant)
            _staticRules = new TenantOidcRules
            {
                TenantId = "webapi",
                Providers = new Dictionary<string, OidcProviderRule>
                {
                    [providerName] = providerRule
                },
                Notes = "Static configuration from appsettings.json for WebAPI"
            };

            _logger.LogInformation("Loaded static OIDC configuration: Provider={ProviderName}, Authority={Authority}, Audience={Audience}", 
                providerName, authority, string.Join(", ", expectedAudience));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Oidc configuration from appsettings.json");
            throw new InvalidOperationException("Invalid Oidc configuration in appsettings.json", ex);
        }
    }

    /// <summary>
    /// Always returns the static configuration from appsettings.json (tenantId is ignored).
    /// </summary>
    public Task<ServiceResult<TenantOidcRules?>> GetForTenantAsync(string tenantId)
    {
        if (_staticRules == null)
        {
            return Task.FromResult(ServiceResult<TenantOidcRules?>.InternalServerError(
                "OIDC configuration is not available. Check appsettings.json for Oidc section."));
        }

        return Task.FromResult(ServiceResult<TenantOidcRules?>.Success(_staticRules));
    }

    /// <summary>
    /// Not supported for static configuration.
    /// </summary>
    public Task<ServiceResult<bool>> UpsertAsync(string tenantId, string jsonConfig, string actorUserId)
    {
        return Task.FromResult(ServiceResult<bool>.BadRequest(
            "Upsert is not supported for static OIDC configuration. Update appsettings.json instead."));
    }

    /// <summary>
    /// Not supported for static configuration.
    /// </summary>
    public Task<ServiceResult<bool>> DeleteAsync(string tenantId)
    {
        return Task.FromResult(ServiceResult<bool>.BadRequest(
            "Delete is not supported for static OIDC configuration. Update appsettings.json instead."));
    }

    /// <summary>
    /// Returns all configurations (just the single static one).
    /// </summary>
    public Task<ServiceResult<List<(string tenantId, TenantOidcRules? rules)>>> GetAllAsync()
    {
        var list = new List<(string tenantId, TenantOidcRules? rules)>();
        
        if (_staticRules != null)
        {
            list.Add(("webapi", _staticRules));
        }

        return Task.FromResult(ServiceResult<List<(string tenantId, TenantOidcRules? rules)>>.Success(list));
    }
}

