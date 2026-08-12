using System.ComponentModel.DataAnnotations;
using Shared.Data.Models;
using Xunit;

namespace Tests.UnitTests.Shared.Data.Models;

/// <summary>
/// Tests for tenant ID validation, in particular the lowercase rule that applies only when a
/// tenant is created. Lookups must stay permissive so tenants created before the rule, which may
/// contain uppercase characters, remain reachable.
/// </summary>
public class TenantIdValidationTests
{
    [Theory]
    [InlineData("acme-corp")]
    [InlineData("parkly.no")]
    [InlineData("tenant-99")]
    public void SanitizeAndValidateNewTenantId_AcceptsLowercaseIds(string tenantId)
    {
        Assert.Equal(tenantId, Tenant.SanitizeAndValidateNewTenantId(tenantId));
    }

    [Theory]
    [InlineData("Acme-Corp")]
    [InlineData("ACME")]
    [InlineData("parkly.NO")]
    public void SanitizeAndValidateNewTenantId_RejectsIdsContainingUppercase(string tenantId)
    {
        var exception = Assert.Throws<ValidationException>(
            () => Tenant.SanitizeAndValidateNewTenantId(tenantId));

        // The message names the accepted form so the caller can correct the request directly.
        Assert.Contains(tenantId.ToLowerInvariant(), exception.Message);
    }

    [Fact]
    public void SanitizeAndValidateNewTenantId_RejectsBlankId()
    {
        Assert.Throws<ValidationException>(() => Tenant.SanitizeAndValidateNewTenantId("   "));
    }

    [Fact]
    public void SanitizeAndValidateTenantId_StillAcceptsUppercase_SoLegacyTenantsStayReachable()
    {
        Assert.Equal("Acme-Corp", Tenant.SanitizeAndValidateTenantId("Acme-Corp"));
    }
}
