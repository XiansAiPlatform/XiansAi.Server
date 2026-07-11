using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using Shared.Data.Models;
using Xunit;

namespace Tests.UnitTests.Shared.Data.Models;

public class TenantMetadataTests
{
    private static Tenant CreateValidTenant(List<TenantMetadata>? metadata = null)
    {
        return new Tenant
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = "test-tenant",
            Name = "Test Tenant",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "unit-test",
            Metadata = metadata
        };
    }

    [Fact]
    public void Type_DefaultsToPlainText()
    {
        var entry = new TenantMetadata { Key = "Region", Value = "WestEurope" };

        Assert.Equal(MetadataType.PlainText, entry.Type);
    }

    [Theory]
    [InlineData("bad key")]
    [InlineData("bad!key")]
    [InlineData("bad/key")]
    public void Validate_WithInvalidKeyCharacters_Throws(string key)
    {
        var entry = new TenantMetadata { Key = key, Value = "v" };

        Assert.Throws<ValidationException>(() => entry.Validate());
    }

    [Fact]
    public void Validate_WithTooLongKey_Throws()
    {
        var entry = new TenantMetadata { Key = new string('a', 101), Value = "v" };

        Assert.Throws<ValidationException>(() => entry.Validate());
    }

    [Fact]
    public void Validate_WithValidEntry_Passes()
    {
        var entry = new TenantMetadata { Key = "Open-Ai.Key_1", Value = "any value at all!", Type = MetadataType.Secret };

        entry.Validate();
    }

    [Fact]
    public void SanitizeAndReturn_SanitizesKey_ButNeverTouchesValue()
    {
        // Value may be ciphertext or free text and must pass through byte-for-byte.
        const string value = "  ci+pher/text==with $trange&chars\t ";
        var entry = new TenantMetadata { Key = " OpenAiKey ", Value = value };

        var sanitized = entry.SanitizeAndReturn();

        Assert.Equal("OpenAiKey", sanitized.Key);
        Assert.Equal(value, sanitized.Value);
    }

    [Fact]
    public void Copy_ReturnsIndependentInstance()
    {
        var original = new TenantMetadata { Key = "OpenAiKey", Value = "original", Type = MetadataType.Secret };

        var copy = original.Copy();
        copy.Value = "mutated";
        copy.Type = MetadataType.PlainText;

        Assert.Equal("original", original.Value);
        Assert.Equal(MetadataType.Secret, original.Type);
    }

    [Fact]
    public void TenantValidate_WithDuplicateMetadataKeys_CaseInsensitive_Throws()
    {
        var tenant = CreateValidTenant(new List<TenantMetadata>
        {
            new() { Key = "Region", Value = "WestEurope" },
            new() { Key = "region", Value = "NorthEurope" }
        });

        var ex = Assert.Throws<ValidationException>(() => tenant.Validate());
        Assert.Contains("Duplicate metadata key", ex.Message);
    }

    [Fact]
    public void TenantValidate_WithValidMetadata_Passes()
    {
        var tenant = CreateValidTenant(new List<TenantMetadata>
        {
            new() { Key = "OpenAiKey", Value = "sk-x", Type = MetadataType.Secret },
            new() { Key = "Region", Value = "WestEurope" }
        });

        tenant.Validate();
    }

    [Fact]
    public void TenantValidate_WithoutMetadata_Passes()
    {
        var tenant = CreateValidTenant(metadata: null);

        tenant.Validate();
    }

    [Fact]
    public void TenantValidate_WithInvalidMetadataEntry_Throws()
    {
        var tenant = CreateValidTenant(new List<TenantMetadata>
        {
            new() { Key = "invalid key!", Value = "v" }
        });

        Assert.Throws<ValidationException>(() => tenant.Validate());
    }

    [Fact]
    public void ShallowCopy_DeepCopiesMetadata_SoCopyMutationDoesNotAffectOriginal()
    {
        // The cache returns ShallowCopy() results; callers must not be able to
        // mutate cached metadata through the copy.
        var tenant = CreateValidTenant(new List<TenantMetadata>
        {
            new() { Key = "OpenAiKey", Value = "ciphertext", Type = MetadataType.Secret }
        });

        var copy = tenant.ShallowCopy();
        copy.Metadata![0].Value = "mutated";
        copy.Metadata.Add(new TenantMetadata { Key = "Extra", Value = "x" });

        Assert.Equal("ciphertext", tenant.Metadata![0].Value);
        Assert.Single(tenant.Metadata);
    }

    [Fact]
    public void ShallowCopy_WithNullMetadata_KeepsNull()
    {
        var tenant = CreateValidTenant(metadata: null);

        var copy = tenant.ShallowCopy();

        Assert.Null(copy.Metadata);
    }

    [Fact]
    public void SanitizeAndReturn_CarriesMetadataThrough()
    {
        // SanitizeAndReturn enumerates properties manually; metadata must not be dropped
        // (it is called on every create/update via SanitizeAndValidate).
        var tenant = CreateValidTenant(new List<TenantMetadata>
        {
            new() { Key = "OpenAiKey", Value = "ciphertext", Type = MetadataType.Secret }
        });

        var sanitized = tenant.SanitizeAndReturn();

        Assert.NotNull(sanitized.Metadata);
        Assert.Single(sanitized.Metadata!);
        Assert.Equal("OpenAiKey", sanitized.Metadata![0].Key);
        Assert.Equal("ciphertext", sanitized.Metadata[0].Value);
        Assert.Equal(MetadataType.Secret, sanitized.Metadata[0].Type);
    }
}
