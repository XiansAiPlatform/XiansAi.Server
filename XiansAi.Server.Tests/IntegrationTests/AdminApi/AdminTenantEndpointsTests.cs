using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shared.Data.Models;
using Xunit;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

public class AdminTenantEndpointsTests : AdminApiIntegrationTestBase
{
    // A valid 1x1 transparent PNG encoded as base64 (no data-URI prefix).
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    public AdminTenantEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task ListTenants_WithValidRequest_ReturnsPaginatedTenantList()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);

        await CreateTestTenantAsync(tenantId);
        await CreateTestTenantAsync($"tenant-2-{Guid.NewGuid()}");

        // Act
        var response = await GetAsync("/api/v1/admin/tenants");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;

        // Response is a paginated envelope: { tenants: [...], pagination: {...} }
        Assert.Equal(JsonValueKind.Array, root.GetProperty("tenants").ValueKind);
        var pagination = root.GetProperty("pagination");
        Assert.Equal(1, pagination.GetProperty("page").GetInt32());
        Assert.Equal(20, pagination.GetProperty("pageSize").GetInt32());
        Assert.True(pagination.GetProperty("totalItems").GetInt32() >= 2);
    }

    [Fact]
    public async Task ListTenants_WithPageSize_LimitsResultsAndReportsPagination()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);

        await CreateTestTenantAsync(tenantId);
        await CreateTestTenantAsync($"tenant-2-{Guid.NewGuid()}");
        await CreateTestTenantAsync($"tenant-3-{Guid.NewGuid()}");

        // Act - request a single item per page
        var response = await GetAsync("/api/v1/admin/tenants?page=1&pageSize=1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;

        Assert.Equal(1, root.GetProperty("tenants").GetArrayLength());

        var pagination = root.GetProperty("pagination");
        Assert.Equal(1, pagination.GetProperty("page").GetInt32());
        Assert.Equal(1, pagination.GetProperty("pageSize").GetInt32());
        Assert.True(pagination.GetProperty("totalItems").GetInt32() >= 3);
        Assert.True(pagination.GetProperty("hasNext").GetBoolean());
        Assert.False(pagination.GetProperty("hasPrevious").GetBoolean());
    }

    [Fact]
    public async Task GetTenantByTenantId_WithValidId_ReturnsTenant()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);

        var tenant = await CreateTestTenantAsync(tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await ReadAsJsonAsync<Tenant>(response);
        Assert.NotNull(result);
        Assert.Equal(tenant.TenantId, result.TenantId);
    }

    [Fact]
    public async Task GetTenantByTenantId_WithBase64Logo_ReturnsLogoUrlInsteadOfBase64()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);

        var logo = new Logo { ImgBase64 = OnePixelPngBase64, Width = 1, Height = 1 };
        await CreateTestTenantAsync(tenantId, logo: logo);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(OnePixelPngBase64, content);

        var result = await ReadAsJsonAsync<Tenant>(response);
        Assert.NotNull(result);
        Assert.NotNull(result.Logo);
        Assert.Null(result.Logo!.ImgBase64);
        Assert.False(string.IsNullOrEmpty(result.Logo.Url));
        Assert.Contains($"/tenants/{tenantId}/logo", result.Logo.Url!);
    }

    [Fact]
    public async Task GetTenantLogo_WithBase64Logo_ReturnsImageBytes()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);

        var logo = new Logo { ImgBase64 = OnePixelPngBase64, Width = 1, Height = 1 };
        await CreateTestTenantAsync(tenantId, logo: logo);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/logo");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Convert.FromBase64String(OnePixelPngBase64), bytes);
    }

    [Fact]
    public async Task GetTenantLogo_WithoutLogo_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);

        await CreateTestTenantAsync(tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/logo");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTenantByTenantId_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);

        var invalidTenantId = $"non-existent-{Guid.NewGuid()}";

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{invalidTenantId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenant_WithValidRequest_CreatesTenant()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId); // Required: X-Tenant-Id header must reference an existing tenant for auth validation

        var request = new
        {
            tenantId = $"new-tenant-{Guid.NewGuid()}",
            name = "New Test Tenant",
            domain = $"new-tenant-{Guid.NewGuid()}.test.com"
        };

        // Act
        var response = await PostAsJsonAsync("/api/v1/admin/tenants", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The endpoint returns a TenantCreatedResult wrapper: { tenant, location }.
        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);
        var tenantId2 = json.RootElement.GetProperty("tenant").GetProperty("tenantId").GetString();
        Assert.Equal(request.tenantId, tenantId2);
    }

    [Fact]
    public async Task CreateTenant_WithUppercaseTenantId_ReturnsBadRequest()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId); // Required: X-Tenant-Id header must reference an existing tenant for auth validation

        var newTenantId = $"New-Tenant-{Guid.NewGuid()}";
        var request = new
        {
            tenantId = newTenantId,
            name = "Uppercase Tenant",
            domain = $"uppercase-{Guid.NewGuid()}.test.com"
        };

        // Act
        var response = await PostAsJsonAsync("/api/v1/admin/tenants", request);

        // Assert - rejected rather than silently lowercased, and told what to send instead
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("must be lowercase", content);
        Assert.Contains(newTenantId.ToLowerInvariant(), content);
    }

    [Fact]
    public async Task CreateTenant_WhenAnExistingTenantDiffersOnlyByCase_ReturnsBadRequest()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId); // Required: X-Tenant-Id header must reference an existing tenant for auth validation

        // Seeded directly to represent a tenant created before new ids had to be lowercase.
        var legacyTenantId = $"Legacy-Tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(legacyTenantId);

        var request = new
        {
            tenantId = legacyTenantId.ToLowerInvariant(),
            name = "Duplicate Tenant",
            domain = $"other-{Guid.NewGuid()}.test.com"
        };

        // Act
        var response = await PostAsJsonAsync("/api/v1/admin/tenants", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("already exists", content);
    }

    [Fact]
    public async Task UpdateTenant_WithValidRequest_UpdatesTenant()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);

        var tenant = await CreateTestTenantAsync(tenantId);

        var request = new
        {
            name = "Updated Tenant Name",
            description = "Updated description"
        };

        // Act
        var response = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await ReadAsJsonAsync<Tenant>(response);
        Assert.NotNull(result);
        Assert.Equal(request.name, result.Name);
    }

    [Fact]
    public async Task DeleteTenant_WithValidId_DeletesTenant()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);

        var tenant = await CreateTestTenantAsync(tenantId);

        // Act
        var response = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}");

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteTenant_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);

        var invalidTenantId = $"non-existent-{Guid.NewGuid()}";

        // Act
        var response = await DeleteAsync($"/api/v1/admin/tenants/{invalidTenantId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenant_WithMetadata_EncryptsSecretAtRest_AndNeverExposesItInTenantPayloads()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        const string secretValue = "sk-super-secret-openai-key";
        var newTenantId = $"new-tenant-{Guid.NewGuid()}";
        var request = new
        {
            tenantId = newTenantId,
            name = "Tenant With Metadata",
            domain = $"{newTenantId}.test.com",
            metadata = new[]
            {
                new { key = "OpenAiKey", value = secretValue, type = "Secret" },
                new { key = "Region", value = "WestEurope", type = "PlainText" }
            }
        };

        // Act
        var response = await PostAsJsonAsync("/api/v1/admin/tenants", request);

        // Assert - the create response never carries the decrypted secret
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secretValue, content);
        using var json = JsonDocument.Parse(content);
        var metadata = json.RootElement.GetProperty("tenant").GetProperty("metadata");
        Assert.Equal(2, metadata.GetArrayLength());
        Assert.Equal("WestEurope", GetMetadataValue(metadata, "Region"));

        // Assert - the stored document has the Secret value encrypted, PlainText verbatim
        var stored = await GetStoredTenantAsync(newTenantId);
        Assert.NotNull(stored.Metadata);
        var storedSecret = stored.Metadata!.Single(m => m.Key == "OpenAiKey");
        Assert.NotEqual(secretValue, storedSecret.Value);
        Assert.Equal(MetadataType.Secret, storedSecret.Type);
        var storedPlain = stored.Metadata!.Single(m => m.Key == "Region");
        Assert.Equal("WestEurope", storedPlain.Value);
        Assert.Equal(MetadataType.PlainText, storedPlain.Type);

        // Assert - GET tenant never carries the decrypted secret either
        var getResponse = await GetAsync($"/api/v1/admin/tenants/{newTenantId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getContent = await getResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secretValue, getContent);
    }

    [Fact]
    public async Task GetTenantMetadata_ReturnsDecryptedValues()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        const string secretValue = "sk-super-secret-openai-key";
        var newTenantId = $"new-tenant-{Guid.NewGuid()}";
        var request = new
        {
            tenantId = newTenantId,
            name = "Tenant With Metadata",
            domain = $"{newTenantId}.test.com",
            metadata = new[]
            {
                new { key = "OpenAiKey", value = secretValue, type = "Secret" },
                new { key = "Region", value = "WestEurope", type = "PlainText" }
            }
        };
        var createResponse = await PostAsJsonAsync("/api/v1/admin/tenants", request);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        // Act - the dedicated metadata endpoint is the only place secrets are decrypted
        var response = await GetAsync($"/api/v1/admin/tenants/{newTenantId}/metadata");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var metadata = await ReadAsJsonAsync<List<TenantMetadata>>(response);
        Assert.NotNull(metadata);
        Assert.Equal(2, metadata!.Count);
        Assert.Equal(secretValue, metadata.Single(m => m.Key == "OpenAiKey").Value);
        Assert.Equal(MetadataType.Secret, metadata.Single(m => m.Key == "OpenAiKey").Type);
        Assert.Equal("WestEurope", metadata.Single(m => m.Key == "Region").Value);
    }

    [Fact]
    public async Task GetTenantMetadataByKey_ReturnsDecryptedValue()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        const string secretValue = "sk-super-secret-openai-key";
        var request = new
        {
            metadata = new[]
            {
                new { key = "OpenAiKey", value = secretValue, type = "Secret" },
                new { key = "Region", value = "WestEurope", type = "PlainText" }
            }
        };
        var patchResponse = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}", request);
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        // Act - lookup is case-insensitive
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/metadata/openaikey");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await ReadAsJsonAsync<TenantMetadata>(response);
        Assert.NotNull(entry);
        Assert.Equal("OpenAiKey", entry!.Key);
        Assert.Equal(secretValue, entry.Value);
        Assert.Equal(MetadataType.Secret, entry.Type);

        // Act + Assert - PlainText entry comes back verbatim
        var plainResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/metadata/Region");
        Assert.Equal(HttpStatusCode.OK, plainResponse.StatusCode);
        var plainEntry = await ReadAsJsonAsync<TenantMetadata>(plainResponse);
        Assert.Equal("WestEurope", plainEntry!.Value);
    }

    [Fact]
    public async Task GetTenantMetadataByKey_WithUnknownKey_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/metadata/no-such-key");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpsertTenantMetadata_AddsNewSecretEntry()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        const string secretValue = "sk-upserted-secret";
        var request = new { value = secretValue, type = "Secret" };

        // Act
        var response = await PutAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/metadata/OpenAiKey", request);

        // Assert - response echoes the entry as provided
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await ReadAsJsonAsync<TenantMetadata>(response);
        Assert.NotNull(entry);
        Assert.Equal("OpenAiKey", entry!.Key);
        Assert.Equal(secretValue, entry.Value);
        Assert.Equal(MetadataType.Secret, entry.Type);

        // Assert - stored encrypted
        var stored = await GetStoredTenantAsync(tenantId);
        var storedEntry = stored.Metadata!.Single(m => m.Key == "OpenAiKey");
        Assert.NotEqual(secretValue, storedEntry.Value);

        // Assert - dedicated endpoint decrypts it
        var getResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/metadata/OpenAiKey");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await ReadAsJsonAsync<TenantMetadata>(getResponse);
        Assert.Equal(secretValue, fetched!.Value);
    }

    [Fact]
    public async Task UpsertTenantMetadata_UpdatesExistingKey_AndPreservesOtherEntries()
    {
        // Arrange - seed two entries via PATCH
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var seed = new
        {
            metadata = new[]
            {
                new { key = "OpenAiKey", value = "original-secret", type = "Secret" },
                new { key = "Region", value = "WestEurope", type = "PlainText" }
            }
        };
        var seedResponse = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}", seed);
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);
        var seededRegion = (await GetStoredTenantAsync(tenantId)).Metadata!.Single(m => m.Key == "Region");

        // Act - update by key, case-insensitive, changing value and type
        var request = new { value = "NorthEurope", type = "PlainText" };
        var response = await PutAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/metadata/region", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await GetStoredTenantAsync(tenantId);
        Assert.Equal(2, stored.Metadata!.Count);
        Assert.Equal("NorthEurope", stored.Metadata!.Single(m => string.Equals(m.Key, "region", StringComparison.OrdinalIgnoreCase)).Value);

        // The untouched Secret entry keeps its exact stored ciphertext
        var untouched = stored.Metadata!.Single(m => m.Key == "OpenAiKey");
        Assert.NotEqual("original-secret", untouched.Value);
        Assert.Equal(MetadataType.Secret, untouched.Type);
    }

    [Fact]
    public async Task UpsertTenantMetadata_WithInvalidKey_ReturnsBadRequest()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var request = new { value = "some-value", type = "PlainText" };

        // Act - key contains characters outside ^[a-zA-Z0-9._-]+$
        var response = await PutAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/metadata/bad%20key%21", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTenantMetadata_RemovesEntry_AndPreservesOthers()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var seed = new
        {
            metadata = new[]
            {
                new { key = "OpenAiKey", value = "secret-to-delete", type = "Secret" },
                new { key = "Region", value = "WestEurope", type = "PlainText" }
            }
        };
        var seedResponse = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}", seed);
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);

        // Act - delete by key, case-insensitive
        var response = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/metadata/openaikey");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await GetStoredTenantAsync(tenantId);
        Assert.Single(stored.Metadata!);
        Assert.Equal("Region", stored.Metadata![0].Key);

        var getResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/metadata/OpenAiKey");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteTenantMetadata_WithUnknownKey_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        // Act
        var response = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/metadata/no-such-key");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTenantMetadata_ForTenantWithoutMetadata_ReturnsEmptyList()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/metadata");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var metadata = await ReadAsJsonAsync<List<TenantMetadata>>(response);
        Assert.NotNull(metadata);
        Assert.Empty(metadata!);
    }

    [Fact]
    public async Task UpdateTenant_WithMetadata_ReplacesIt_AndOmittingMetadataPreservesIt()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        const string secretValue = "patched-secret-value";
        var request = new
        {
            metadata = new[]
            {
                new { key = "OpenAiKey", value = secretValue, type = "Secret" }
            }
        };

        // Act - PATCH with metadata
        var response = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}", request);

        // Assert - the update response never carries the decrypted secret; stored value is encrypted
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secretValue, responseContent);

        var stored = await GetStoredTenantAsync(tenantId);
        Assert.NotNull(stored.Metadata);
        Assert.NotEqual(secretValue, stored.Metadata!.Single(m => m.Key == "OpenAiKey").Value);

        // Assert - the dedicated metadata endpoint returns the new value decrypted
        var metadataResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/metadata");
        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);
        var decrypted = await ReadAsJsonAsync<List<TenantMetadata>>(metadataResponse);
        Assert.NotNull(decrypted);
        Assert.Equal(secretValue, decrypted!.Single(m => m.Key == "OpenAiKey").Value);

        // Act - PATCH without metadata must preserve the existing metadata untouched
        var nameOnlyResponse = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}", new { name = "Renamed Tenant" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, nameOnlyResponse.StatusCode);
        var afterNameUpdate = await GetStoredTenantAsync(tenantId);
        Assert.NotNull(afterNameUpdate.Metadata);
        Assert.Equal(
            stored.Metadata!.Single(m => m.Key == "OpenAiKey").Value,
            afterNameUpdate.Metadata!.Single(m => m.Key == "OpenAiKey").Value);
    }

    [Fact]
    public async Task CreateTenant_WithDuplicateMetadataKeys_ReturnsBadRequest()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var duplicateTenantId = $"new-tenant-{Guid.NewGuid()}";
        var request = new
        {
            tenantId = duplicateTenantId,
            name = "Tenant With Duplicate Metadata",
            domain = $"{duplicateTenantId}.test.com",
            metadata = new[]
            {
                new { key = "Region", value = "WestEurope", type = "PlainText" },
                new { key = "region", value = "NorthEurope", type = "PlainText" }
            }
        };

        // Act
        var response = await PostAsJsonAsync("/api/v1/admin/tenants", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Reads the tenant document as persisted in MongoDB (bypassing the service layer decryption).
    /// </summary>
    private async Task<Tenant> GetStoredTenantAsync(string tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var tenantRepository = scope.ServiceProvider.GetRequiredService<Shared.Repositories.ITenantRepository>();
        var tenant = await tenantRepository.GetByTenantIdAsync(tenantId);
        Assert.NotNull(tenant);
        return tenant!;
    }

    private static string? GetMetadataValue(JsonElement metadataArray, string key)
    {
        foreach (var item in metadataArray.EnumerateArray())
        {
            if (item.GetProperty("key").GetString() == key)
            {
                return item.GetProperty("value").GetString();
            }
        }
        return null;
    }
}
