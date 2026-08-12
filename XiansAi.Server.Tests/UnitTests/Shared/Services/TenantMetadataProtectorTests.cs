using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Data.Models;
using Shared.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

public class TenantMetadataProtectorTests
{
    private const string BaseSecret = "unit-test-base-secret-min-32-chars-padding-padding";
    private const string TenantId = "test-tenant";

    private readonly TenantMetadataProtector _protector;

    public TenantMetadataProtectorTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionKeys:BaseSecret"] = BaseSecret
            })
            .Build();
        var encryption = new SecureEncryptionService(NullLogger<SecureEncryptionService>.Instance, configuration);
        _protector = new TenantMetadataProtector(encryption);
    }

    [Fact]
    public void Protect_EncryptsSecretValues_AndUnprotectRoundTrips()
    {
        var metadata = new List<TenantMetadata>
        {
            new() { Key = "OpenAiKey", Value = "sk-super-secret", Type = MetadataType.Secret }
        };

        var protectedList = _protector.Protect(metadata, TenantId);

        Assert.NotNull(protectedList);
        Assert.NotEqual("sk-super-secret", protectedList[0].Value);
        // Ciphertext is base64 (nonce | tag | cipher)
        Assert.NotEmpty(Convert.FromBase64String(protectedList[0].Value));

        var unprotectedList = _protector.Unprotect(protectedList, TenantId);

        Assert.NotNull(unprotectedList);
        Assert.Equal("sk-super-secret", unprotectedList[0].Value);
        Assert.Equal("OpenAiKey", unprotectedList[0].Key);
        Assert.Equal(MetadataType.Secret, unprotectedList[0].Type);
    }

    [Fact]
    public void Protect_LeavesPlainTextValuesUntouched()
    {
        var metadata = new List<TenantMetadata>
        {
            new() { Key = "Region", Value = "WestEurope", Type = MetadataType.PlainText }
        };

        var protectedList = _protector.Protect(metadata, TenantId);

        Assert.NotNull(protectedList);
        Assert.Equal("WestEurope", protectedList[0].Value);
    }

    [Fact]
    public void Protect_DoesNotMutateInputList()
    {
        var item = new TenantMetadata { Key = "OpenAiKey", Value = "sk-super-secret", Type = MetadataType.Secret };
        var metadata = new List<TenantMetadata> { item };

        _protector.Protect(metadata, TenantId);

        Assert.Equal("sk-super-secret", item.Value);
    }

    [Fact]
    public void ProtectAndUnprotect_PassThroughNullAndEmptyLists()
    {
        Assert.Null(_protector.Protect(null, TenantId));
        Assert.Null(_protector.Unprotect(null, TenantId));

        var empty = new List<TenantMetadata>();
        Assert.Same(empty, _protector.Protect(empty, TenantId));
        Assert.Same(empty, _protector.Unprotect(empty, TenantId));
    }

    [Fact]
    public void Unprotect_WithWrongTenantId_Throws_WithoutLeakingValue()
    {
        var metadata = new List<TenantMetadata>
        {
            new() { Key = "OpenAiKey", Value = "sk-super-secret", Type = MetadataType.Secret }
        };
        var protectedList = _protector.Protect(metadata, TenantId);

        var ex = Assert.Throws<InvalidOperationException>(
            () => _protector.Unprotect(protectedList, "another-tenant"));

        Assert.Contains("OpenAiKey", ex.Message);
        Assert.DoesNotContain("sk-super-secret", ex.Message);
        Assert.DoesNotContain(protectedList![0].Value, ex.Message);
    }

    [Fact]
    public void Unprotect_WithCorruptedCiphertext_Throws_WithoutLeakingValue()
    {
        var metadata = new List<TenantMetadata>
        {
            new() { Key = "OpenAiKey", Value = "not-a-valid-ciphertext", Type = MetadataType.Secret }
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => _protector.Unprotect(metadata, TenantId));

        Assert.Contains("OpenAiKey", ex.Message);
        Assert.DoesNotContain("not-a-valid-ciphertext", ex.Message);
    }

    [Fact]
    public void Protect_WithEmptySecretValue_RoundTrips()
    {
        var metadata = new List<TenantMetadata>
        {
            new() { Key = "EmptySecret", Value = string.Empty, Type = MetadataType.Secret }
        };

        var protectedList = _protector.Protect(metadata, TenantId);
        Assert.NotEqual(string.Empty, protectedList![0].Value);

        var unprotectedList = _protector.Unprotect(protectedList, TenantId);
        Assert.Equal(string.Empty, unprotectedList![0].Value);
    }

    [Fact]
    public void Protect_WithoutTenantId_Throws()
    {
        var metadata = new List<TenantMetadata>
        {
            new() { Key = "OpenAiKey", Value = "sk-super-secret", Type = MetadataType.Secret }
        };

        Assert.Throws<ArgumentException>(() => _protector.Protect(metadata, ""));
    }
}
