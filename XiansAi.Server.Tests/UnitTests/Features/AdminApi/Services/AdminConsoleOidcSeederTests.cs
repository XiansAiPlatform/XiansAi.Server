using Features.AdminApi.Auth;
using Features.AdminApi.Services;
using Shared.Configuration;
using Xunit;

namespace Tests.UnitTests.Features.AdminApi.Services;

public class AdminConsoleOidcSeederTests
{
    [Fact]
    public void NoProvidersConfigured_ReturnsNull()
    {
        var rules = AdminConsoleOidcSeeder.BuildRules(new AdminConsoleOidcSettings());

        Assert.Null(rules);
    }

    [Fact]
    public void EmptyProvidersDictionary_ReturnsNull()
    {
        var settings = new AdminConsoleOidcSettings { Providers = new() };

        var rules = AdminConsoleOidcSeeder.BuildRules(settings);

        Assert.Null(rules);
    }

    [Fact]
    public void ProviderMissingAuthority_IsSkipped()
    {
        var settings = new AdminConsoleOidcSettings
        {
            Providers = new()
            {
                ["google"] = new AdminConsoleOidcProviderSettings { ClientId = "abc" }
            }
        };

        var rules = AdminConsoleOidcSeeder.BuildRules(settings);

        Assert.Null(rules);
    }

    [Fact]
    public void ProviderMissingClientId_IsSkipped()
    {
        var settings = new AdminConsoleOidcSettings
        {
            Providers = new()
            {
                ["google"] = new AdminConsoleOidcProviderSettings { Authority = "https://accounts.google.com" }
            }
        };

        var rules = AdminConsoleOidcSeeder.BuildRules(settings);

        Assert.Null(rules);
    }

    [Fact]
    public void OneValidProviderAmongInvalidOnes_KeepsOnlyTheValidOne()
    {
        var settings = new AdminConsoleOidcSettings
        {
            Providers = new()
            {
                ["google"] = new AdminConsoleOidcProviderSettings
                {
                    Authority = "https://accounts.google.com",
                    ClientId = "google-client-id"
                },
                ["broken"] = new AdminConsoleOidcProviderSettings { ClientId = "no-authority" }
            }
        };

        var rules = AdminConsoleOidcSeeder.BuildRules(settings);

        Assert.NotNull(rules);
        Assert.Single(rules!.Providers!);
        Assert.True(rules.Providers!.ContainsKey("google"));
    }

    [Fact]
    public void UsesTheAdminConsolePseudoTenantId()
    {
        var settings = new AdminConsoleOidcSettings
        {
            Providers = new()
            {
                ["google"] = new AdminConsoleOidcProviderSettings
                {
                    Authority = "https://accounts.google.com",
                    ClientId = "google-client-id"
                }
            }
        };

        var rules = AdminConsoleOidcSeeder.BuildRules(settings);

        Assert.Equal(AdminActingUserResolver.AdminConsolePseudoTenant, rules!.TenantId);
    }

    [Fact]
    public void IssuerDefaultsToAuthority_WhenNotSetSeparately()
    {
        var settings = new AdminConsoleOidcSettings
        {
            Providers = new()
            {
                ["azure-ad"] = new AdminConsoleOidcProviderSettings
                {
                    Authority = "https://login.microsoftonline.com/tenant-id/v2.0",
                    ClientId = "azure-client-id"
                }
            }
        };

        var rules = AdminConsoleOidcSeeder.BuildRules(settings);

        Assert.Equal("https://login.microsoftonline.com/tenant-id/v2.0", rules!.Providers!["azure-ad"].Issuer);
    }

    [Fact]
    public void ExplicitIssuer_OverridesAuthorityDefault()
    {
        var settings = new AdminConsoleOidcSettings
        {
            Providers = new()
            {
                ["azure-ad"] = new AdminConsoleOidcProviderSettings
                {
                    Authority = "https://login.microsoftonline.com/tenant-id/v2.0",
                    Issuer = "https://sts.windows.net/tenant-id/",
                    ClientId = "azure-client-id"
                }
            }
        };

        var rules = AdminConsoleOidcSeeder.BuildRules(settings);

        Assert.Equal("https://sts.windows.net/tenant-id/", rules!.Providers!["azure-ad"].Issuer);
    }

    [Fact]
    public void ClientId_BecomesTheSoleExpectedAudience()
    {
        var settings = new AdminConsoleOidcSettings
        {
            Providers = new()
            {
                ["google"] = new AdminConsoleOidcProviderSettings
                {
                    Authority = "https://accounts.google.com",
                    ClientId = "google-client-id"
                }
            }
        };

        var rules = AdminConsoleOidcSeeder.BuildRules(settings);

        Assert.Equal(new List<string> { "google-client-id" }, rules!.Providers!["google"].ExpectedAudience);
    }

    [Fact]
    public void MissingScope_StaysUnset_SoDynamicOidcValidatorSkipsTheScopeCheck()
    {
        // DynamicOidcValidator only requires a scope/scp claim when Scope is non-empty
        // (OidcTokenInspector.DescribeMissingScope) - and a real ID token normally carries no
        // such claim, so defaulting this to a non-empty value would reject every real login.
        var settings = new AdminConsoleOidcSettings
        {
            Providers = new()
            {
                ["google"] = new AdminConsoleOidcProviderSettings
                {
                    Authority = "https://accounts.google.com",
                    ClientId = "google-client-id"
                }
            }
        };

        var rules = AdminConsoleOidcSeeder.BuildRules(settings);

        Assert.Null(rules!.Providers!["google"].Scope);
    }

    [Fact]
    public void ExplicitScope_IsPreserved()
    {
        var settings = new AdminConsoleOidcSettings
        {
            Providers = new()
            {
                ["google"] = new AdminConsoleOidcProviderSettings
                {
                    Authority = "https://accounts.google.com",
                    ClientId = "google-client-id",
                    Scope = "openid email"
                }
            }
        };

        var rules = AdminConsoleOidcSeeder.BuildRules(settings);

        Assert.Equal("openid email", rules!.Providers!["google"].Scope);
    }

    [Fact]
    public void MultipleValidProviders_AreAllIncluded()
    {
        var settings = new AdminConsoleOidcSettings
        {
            Providers = new()
            {
                ["google"] = new AdminConsoleOidcProviderSettings
                {
                    Authority = "https://accounts.google.com",
                    ClientId = "google-client-id"
                },
                ["azure-ad"] = new AdminConsoleOidcProviderSettings
                {
                    Authority = "https://login.microsoftonline.com/tenant-id/v2.0",
                    ClientId = "azure-client-id"
                },
                ["okta"] = new AdminConsoleOidcProviderSettings
                {
                    Authority = "https://example.okta.com/oauth2/default",
                    ClientId = "okta-client-id"
                }
            }
        };

        var rules = AdminConsoleOidcSeeder.BuildRules(settings);

        Assert.Equal(3, rules!.Providers!.Count);
        Assert.Equal(3, rules.AllowedProviders!.Count);
    }
}
