using Features.WebApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

/// <summary>
/// Unit tests for the metadata-related logic in TenantService. Repository, cache and
/// context are mocked; encryption uses the real TenantMetadataProtector +
/// SecureEncryptionService so ciphertext behavior is genuine.
/// </summary>
public class TenantServiceMetadataTests
{
    private const string BaseSecret = "unit-test-base-secret-min-32-chars-padding-padding";
    private const string TenantId = "test-tenant";
    private const string SecretValue = "sk-super-secret";

    private readonly Mock<ITenantRepository> _repo = new();
    private readonly Mock<ITenantCacheService> _cache = new();
    private readonly Mock<ITenantContext> _context = new();
    private readonly Mock<IRoleManagementService> _roles = new();
    private readonly Mock<IWebhookEventPublisher> _webhooks = new();
    private readonly TenantMetadataProtector _protector;
    private readonly TenantService _service;

    public TenantServiceMetadataTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionKeys:BaseSecret"] = BaseSecret
            })
            .Build();
        _protector = new TenantMetadataProtector(
            new SecureEncryptionService(NullLogger<SecureEncryptionService>.Instance, configuration));

        _context.Setup(x => x.UserRoles).Returns(new[] { SystemRoles.SysAdmin });
        _context.Setup(x => x.AuthorizedTenantIds).Returns(new List<string> { TenantId });
        _context.Setup(x => x.TenantId).Returns(TenantId);
        _context.Setup(x => x.LoggedInUser).Returns("unit-test-admin");

        _repo.Setup(x => x.CreateAsync(It.IsAny<Tenant>())).Returns(Task.CompletedTask);
        _repo.Setup(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<Tenant>())).ReturnsAsync(true);
        _webhooks.Setup(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _service = new TenantService(
            _repo.Object,
            _cache.Object,
            NullLogger<TenantService>.Instance,
            _context.Object,
            _roles.Object,
            _webhooks.Object,
            _protector);
    }

    private static Tenant CreateStoredTenant(List<TenantMetadata>? metadata = null)
    {
        return new Tenant
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = TenantId,
            Name = "Test Tenant",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "unit-test-admin",
            Metadata = metadata
        };
    }

    private List<TenantMetadata> EncryptedMetadata(params TenantMetadata[] entries)
        => _protector.Protect(entries.ToList(), TenantId)!;

    // ---------- CreateTenant ----------

    [Fact]
    public async Task CreateTenant_WithSecretMetadata_PersistsEncryptedValue_AndPlainTextVerbatim()
    {
        Tenant? persisted = null;
        _repo.Setup(x => x.CreateAsync(It.IsAny<Tenant>()))
            .Callback<Tenant>(t => persisted = t)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateTenant(new CreateTenantRequest
        {
            TenantId = TenantId,
            Name = "Test Tenant",
            Metadata = new List<TenantMetadata>
            {
                new() { Key = "OpenAiKey", Value = SecretValue, Type = MetadataType.Secret },
                new() { Key = "Region", Value = "WestEurope", Type = MetadataType.PlainText }
            }
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(persisted?.Metadata);
        var storedSecret = persisted!.Metadata!.Single(m => m.Key == "OpenAiKey");
        Assert.NotEqual(SecretValue, storedSecret.Value);
        Assert.Equal(SecretValue, _protector.Unprotect(new List<TenantMetadata> { storedSecret }, TenantId)![0].Value);
        Assert.Equal("WestEurope", persisted.Metadata!.Single(m => m.Key == "Region").Value);
    }

    [Fact]
    public async Task CreateTenant_ResponseNeverCarriesPlaintextSecret()
    {
        var result = await _service.CreateTenant(new CreateTenantRequest
        {
            TenantId = TenantId,
            Name = "Test Tenant",
            Metadata = new List<TenantMetadata>
            {
                new() { Key = "OpenAiKey", Value = SecretValue, Type = MetadataType.Secret }
            }
        });

        Assert.True(result.IsSuccess);
        Assert.NotEqual(SecretValue, result.Data!.Tenant.Metadata!.Single().Value);
    }

    [Fact]
    public async Task CreateTenant_WithoutMetadata_PersistsNullMetadata()
    {
        Tenant? persisted = null;
        _repo.Setup(x => x.CreateAsync(It.IsAny<Tenant>()))
            .Callback<Tenant>(t => persisted = t)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateTenant(new CreateTenantRequest
        {
            TenantId = TenantId,
            Name = "Test Tenant"
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(persisted);
        Assert.Null(persisted!.Metadata);
    }

    [Fact]
    public async Task CreateTenant_WithDuplicateMetadataKeys_ReturnsBadRequest()
    {
        var result = await _service.CreateTenant(new CreateTenantRequest
        {
            TenantId = TenantId,
            Name = "Test Tenant",
            Metadata = new List<TenantMetadata>
            {
                new() { Key = "Region", Value = "a" },
                new() { Key = "region", Value = "b" }
            }
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        _repo.Verify(x => x.CreateAsync(It.IsAny<Tenant>()), Times.Never);
    }

    // ---------- UpdateTenant ----------

    [Fact]
    public async Task UpdateTenant_WithMetadata_EncryptsBeforePersist()
    {
        var stored = CreateStoredTenant();
        _repo.Setup(x => x.GetByIdAsync(stored.Id)).ReturnsAsync(stored);
        Tenant? persisted = null;
        _repo.Setup(x => x.UpdateAsync(stored.Id, It.IsAny<Tenant>()))
            .Callback<string, Tenant>((_, t) => persisted = t)
            .ReturnsAsync(true);

        var result = await _service.UpdateTenant(stored.Id, new UpdateTenantRequest
        {
            Metadata = new List<TenantMetadata>
            {
                new() { Key = "OpenAiKey", Value = SecretValue, Type = MetadataType.Secret }
            }
        });

        Assert.True(result.IsSuccess);
        Assert.NotEqual(SecretValue, persisted!.Metadata!.Single().Value);
    }

    [Fact]
    public async Task UpdateTenant_WithoutMetadata_PreservesStoredCiphertextUnchanged()
    {
        var encrypted = EncryptedMetadata(new TenantMetadata { Key = "OpenAiKey", Value = SecretValue, Type = MetadataType.Secret });
        var originalCiphertext = encrypted[0].Value;
        var stored = CreateStoredTenant(encrypted);
        _repo.Setup(x => x.GetByIdAsync(stored.Id)).ReturnsAsync(stored);
        Tenant? persisted = null;
        _repo.Setup(x => x.UpdateAsync(stored.Id, It.IsAny<Tenant>()))
            .Callback<string, Tenant>((_, t) => persisted = t)
            .ReturnsAsync(true);

        var result = await _service.UpdateTenant(stored.Id, new UpdateTenantRequest { Name = "Renamed" });

        Assert.True(result.IsSuccess);
        Assert.Equal(originalCiphertext, persisted!.Metadata!.Single().Value);
    }

    // ---------- GetTenantMetadata ----------

    [Fact]
    public async Task GetTenantMetadata_DecryptsSecret_AndReturnsPlainTextVerbatim()
    {
        var stored = CreateStoredTenant(EncryptedMetadata(
            new TenantMetadata { Key = "OpenAiKey", Value = SecretValue, Type = MetadataType.Secret },
            new TenantMetadata { Key = "Region", Value = "WestEurope", Type = MetadataType.PlainText }));
        _cache.Setup(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(stored);

        var result = await _service.GetTenantMetadata(TenantId);

        Assert.True(result.IsSuccess);
        Assert.Equal(SecretValue, result.Data!.Single(m => m.Key == "OpenAiKey").Value);
        Assert.Equal("WestEurope", result.Data!.Single(m => m.Key == "Region").Value);
    }

    [Fact]
    public async Task GetTenantMetadata_WithoutMetadata_ReturnsEmptyList()
    {
        _cache.Setup(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(CreateStoredTenant());

        var result = await _service.GetTenantMetadata(TenantId);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task GetTenantMetadata_UnknownTenant_ReturnsNotFound()
    {
        _cache.Setup(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync((Tenant?)null);

        var result = await _service.GetTenantMetadata(TenantId);

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task GetTenantMetadata_ForUnauthorizedTenant_ReturnsForbidden()
    {
        _context.Setup(x => x.AuthorizedTenantIds).Returns(new List<string>());

        var result = await _service.GetTenantMetadata(TenantId);

        Assert.Equal(StatusCode.Forbidden, result.StatusCode);
        _cache.Verify(x => x.GetByTenantIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    // ---------- GetTenantMetadataByKey ----------

    [Fact]
    public async Task GetTenantMetadataByKey_IsCaseInsensitive_AndDecrypts()
    {
        var stored = CreateStoredTenant(EncryptedMetadata(
            new TenantMetadata { Key = "OpenAiKey", Value = SecretValue, Type = MetadataType.Secret }));
        _cache.Setup(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(stored);

        var result = await _service.GetTenantMetadataByKey(TenantId, "openaikey");

        Assert.True(result.IsSuccess);
        Assert.Equal("OpenAiKey", result.Data!.Key);
        Assert.Equal(SecretValue, result.Data.Value);
    }

    [Fact]
    public async Task GetTenantMetadataByKey_UnknownKey_ReturnsNotFound()
    {
        _cache.Setup(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(CreateStoredTenant());

        var result = await _service.GetTenantMetadataByKey(TenantId, "missing");

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
    }

    // ---------- UpsertTenantMetadata ----------

    [Fact]
    public async Task UpsertTenantMetadata_AddsNewEntry_EncryptedAtRest()
    {
        var stored = CreateStoredTenant();
        _repo.Setup(x => x.GetByTenantIdAsync(TenantId)).ReturnsAsync(stored);
        Tenant? persisted = null;
        _repo.Setup(x => x.UpdateAsync(stored.Id, It.IsAny<Tenant>()))
            .Callback<string, Tenant>((_, t) => persisted = t)
            .ReturnsAsync(true);

        var result = await _service.UpsertTenantMetadata(TenantId, "OpenAiKey",
            new UpsertTenantMetadataRequest { Value = SecretValue, Type = MetadataType.Secret });

        Assert.True(result.IsSuccess);
        // Response echoes the plaintext entry (metadata endpoints are the decrypted surface)
        Assert.Equal(SecretValue, result.Data!.Value);
        // Persisted value is ciphertext
        Assert.NotEqual(SecretValue, persisted!.Metadata!.Single().Value);
    }

    [Fact]
    public async Task UpsertTenantMetadata_ReplacesExistingKey_CaseInsensitive()
    {
        var stored = CreateStoredTenant(new List<TenantMetadata>
        {
            new() { Key = "Region", Value = "WestEurope", Type = MetadataType.PlainText }
        });
        _repo.Setup(x => x.GetByTenantIdAsync(TenantId)).ReturnsAsync(stored);
        Tenant? persisted = null;
        _repo.Setup(x => x.UpdateAsync(stored.Id, It.IsAny<Tenant>()))
            .Callback<string, Tenant>((_, t) => persisted = t)
            .ReturnsAsync(true);

        var result = await _service.UpsertTenantMetadata(TenantId, "region",
            new UpsertTenantMetadataRequest { Value = "NorthEurope" });

        Assert.True(result.IsSuccess);
        Assert.Single(persisted!.Metadata!);
        Assert.Equal("NorthEurope", persisted.Metadata![0].Value);
    }

    [Fact]
    public async Task UpsertTenantMetadata_WithInvalidKey_ReturnsBadRequest_WithoutPersisting()
    {
        var stored = CreateStoredTenant();
        _repo.Setup(x => x.GetByTenantIdAsync(TenantId)).ReturnsAsync(stored);

        var result = await _service.UpsertTenantMetadata(TenantId, "bad key!",
            new UpsertTenantMetadataRequest { Value = "v" });

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        _repo.Verify(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<Tenant>()), Times.Never);
    }

    [Fact]
    public async Task UpsertTenantMetadata_UnknownTenant_ReturnsNotFound()
    {
        _repo.Setup(x => x.GetByTenantIdAsync(TenantId)).ReturnsAsync((Tenant?)null);

        var result = await _service.UpsertTenantMetadata(TenantId, "OpenAiKey",
            new UpsertTenantMetadataRequest { Value = "v" });

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
    }

    // ---------- DeleteTenantMetadata ----------

    [Fact]
    public async Task DeleteTenantMetadata_RemovesEntry_CaseInsensitive_AndPreservesOthers()
    {
        var stored = CreateStoredTenant(new List<TenantMetadata>
        {
            new() { Key = "OpenAiKey", Value = "cipher", Type = MetadataType.Secret },
            new() { Key = "Region", Value = "WestEurope", Type = MetadataType.PlainText }
        });
        _repo.Setup(x => x.GetByTenantIdAsync(TenantId)).ReturnsAsync(stored);
        Tenant? persisted = null;
        _repo.Setup(x => x.UpdateAsync(stored.Id, It.IsAny<Tenant>()))
            .Callback<string, Tenant>((_, t) => persisted = t)
            .ReturnsAsync(true);

        var result = await _service.DeleteTenantMetadata(TenantId, "openaikey");

        Assert.True(result.IsSuccess);
        Assert.Single(persisted!.Metadata!);
        Assert.Equal("Region", persisted.Metadata![0].Key);
    }

    [Fact]
    public async Task DeleteTenantMetadata_UnknownKey_ReturnsNotFound_WithoutPersisting()
    {
        _repo.Setup(x => x.GetByTenantIdAsync(TenantId)).ReturnsAsync(CreateStoredTenant());

        var result = await _service.DeleteTenantMetadata(TenantId, "missing");

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
        _repo.Verify(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<Tenant>()), Times.Never);
    }
}
