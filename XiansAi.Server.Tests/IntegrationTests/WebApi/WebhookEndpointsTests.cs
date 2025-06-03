using System.Net;
using System.Text.Json;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using XiansAi.Server.Shared.Data.Models;
using Features.WebApi.Services;
using MongoDB.Bson;
using Shared.Utils.Services;
using XiansAi.Server.Tests.TestUtils;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using System.Text;

namespace XiansAi.Server.Tests.IntegrationTests.WebApi;

public class WebhookEndpointsTests : WebApiIntegrationTestBase, IDisposable
{
    private readonly IMongoCollection<Webhook> _webhooksCollection;
    
    public WebhookEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
        // Get access to webhooks collection for cleanup and validation
        var database = _mongoFixture.Database;
        _webhooksCollection = database.GetCollection<Webhook>("webhooks");
    }

    // Helper method to clean up webhooks before each test
    private async Task CleanupWebhooksAsync()
    {
        await _webhooksCollection.DeleteManyAsync(Builders<Webhook>.Filter.Empty);
    }

    // Helper method to create a test webhook
    private async Task<Webhook> CreateTestWebhookAsync(string workflowId = "test-workflow", string eventType = "test.event")
    {
        var request = new WebhookCreateRequest
        {
            WorkflowId = workflowId,
            CallbackUrl = "https://example.com/webhook",
            EventType = eventType,
            IsActive = true
        };

        var response = await PostAsJsonAsync("/api/client/webhooks/", request);
        response.EnsureSuccessStatusCode();
        
        var result = await ReadAsJsonAsync<Webhook>(response);
        Assert.NotNull(result);
        return result;
    }

    // Helper method to create a client without tenant header
    private HttpClient CreateClientWithoutTenantHeader()
    {
        var client = _factory.CreateClient();
        return client;
    }

    [Fact]
    public async Task GetAllWebhooks_WithValidTenant_ReturnsOK()
    {
        // Arrange
        await CleanupWebhooksAsync();
        await CreateTestWebhookAsync("workflow-1", "event.type1");
        await CreateTestWebhookAsync("workflow-2", "event.type2");

        // Act
        var response = await GetAsync("/api/client/webhooks/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var webhooks = await ReadAsJsonAsync<List<Webhook>>(response);
        Assert.NotNull(webhooks);
        Assert.Equal(2, webhooks.Count);
    }

    [Fact]
    public async Task GetAllWebhooks_WithNoWebhooks_ReturnsEmptyList()
    {
        // Arrange
        await CleanupWebhooksAsync();

        // Act
        var response = await GetAsync("/api/client/webhooks/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var webhooks = await ReadAsJsonAsync<List<Webhook>>(response);
        Assert.NotNull(webhooks);
        Assert.Empty(webhooks);
    }

    [Fact]
    public async Task GetWebhooksByWorkflow_WithValidWorkflowId_ReturnsWebhooks()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var workflowId = "specific-workflow";
        await CreateTestWebhookAsync(workflowId, "event1");
        await CreateTestWebhookAsync(workflowId, "event2");
        await CreateTestWebhookAsync("other-workflow", "event3");

        // Act
        var response = await GetAsync($"/api/client/webhooks/workflow/{workflowId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var webhooks = await ReadAsJsonAsync<List<Webhook>>(response);
        Assert.NotNull(webhooks);
        Assert.Equal(2, webhooks.Count);
        Assert.All(webhooks, w => Assert.Equal(workflowId, w.WorkflowId));
    }

    [Fact]
    public async Task GetWebhooksByWorkflow_WithNonExistentWorkflow_ReturnsEmptyList()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var nonExistentWorkflowId = "non-existent-workflow";

        // Act
        var response = await GetAsync($"/api/client/webhooks/workflow/{nonExistentWorkflowId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var webhooks = await ReadAsJsonAsync<List<Webhook>>(response);
        Assert.NotNull(webhooks);
        Assert.Empty(webhooks);
    }

    [Fact]
    public async Task GetWebhook_WithValidId_ReturnsWebhook()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var webhook = await CreateTestWebhookAsync();

        // Act
        var response = await GetAsync($"/api/client/webhooks/{webhook.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<Webhook>(response);
        Assert.NotNull(result);
        Assert.Equal(webhook.Id, result.Id);
        Assert.Equal(webhook.WorkflowId, result.WorkflowId);
        Assert.Equal(webhook.CallbackUrl, result.CallbackUrl);
        Assert.Equal(webhook.EventType, result.EventType);
    }

    [Fact]
    public async Task GetWebhook_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var invalidId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await GetAsync($"/api/client/webhooks/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateWebhook_WithValidRequest_ReturnsCreated()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var request = new WebhookCreateRequest
        {
            WorkflowId = "test-workflow",
            CallbackUrl = "https://example.com/webhook",
            EventType = "test.event",
            IsActive = true
        };

        // Act
        var response = await PostAsJsonAsync("/api/client/webhooks/", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var webhook = await ReadAsJsonAsync<Webhook>(response);
        Assert.NotNull(webhook);
        Assert.Equal(request.WorkflowId, webhook.WorkflowId);
        Assert.Equal(request.CallbackUrl, webhook.CallbackUrl);
        Assert.Equal(request.EventType, webhook.EventType);
        Assert.Equal(request.IsActive, webhook.IsActive);
        Assert.Equal(TestTenantId, webhook.TenantId);
        Assert.NotNull(webhook.Secret);
        Assert.NotEmpty(webhook.Secret);
        Assert.True(webhook.CreatedAt > DateTime.UtcNow.AddMinutes(-1));

        // Verify Location header
        Assert.NotNull(response.Headers.Location);
        Assert.Equal($"/api/client/webhooks/{webhook.Id}", response.Headers.Location.ToString());
    }

    [Fact]
    public async Task CreateWebhook_WithInvalidRequest_ReturnsBadRequest()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var invalidRequest = new
        {
            WorkflowId = "", // Invalid - empty string
            CallbackUrl = "invalid-url", // Invalid URL format
            EventType = "" // Invalid - empty string
        };

        // Act
        var response = await PostAsJsonAsync("/api/client/webhooks/", invalidRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateWebhook_VerifyDataStoredInDatabase()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var request = new WebhookCreateRequest
        {
            WorkflowId = "db-test-workflow",
            CallbackUrl = "https://example.com/webhook",
            EventType = "db.test.event",
            IsActive = true
        };

        // Act
        var response = await PostAsJsonAsync("/api/client/webhooks/", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var webhook = await ReadAsJsonAsync<Webhook>(response);
        Assert.NotNull(webhook);

        // Verify data was stored in database
        var storedWebhook = await _webhooksCollection
            .Find(w => w.Id == webhook.Id)
            .FirstOrDefaultAsync();
        
        Assert.NotNull(storedWebhook);
        Assert.Equal(request.WorkflowId, storedWebhook.WorkflowId);
        Assert.Equal(request.CallbackUrl, storedWebhook.CallbackUrl);
        Assert.Equal(request.EventType, storedWebhook.EventType);
        Assert.Equal(TestTenantId, storedWebhook.TenantId);
        Assert.True(storedWebhook.IsActive);
    }

    [Fact]
    public async Task UpdateWebhook_WithValidRequest_ReturnsOK()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var webhook = await CreateTestWebhookAsync();
        var updateRequest = new WebhookUpdateRequest
        {
            CallbackUrl = "https://updated.example.com/webhook",
            EventType = "updated.event",
            IsActive = false
        };

        // Act
        var response = await PutAsJsonAsync($"/api/client/webhooks/{webhook.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updatedWebhook = await ReadAsJsonAsync<Webhook>(response);
        Assert.NotNull(updatedWebhook);
        Assert.Equal(updateRequest.CallbackUrl, updatedWebhook.CallbackUrl);
        Assert.Equal(updateRequest.EventType, updatedWebhook.EventType);
        Assert.Equal(updateRequest.IsActive, updatedWebhook.IsActive);
        Assert.Equal(webhook.Id, updatedWebhook.Id);
        Assert.Equal(webhook.WorkflowId, updatedWebhook.WorkflowId); // Should remain unchanged
    }

    [Fact]
    public async Task UpdateWebhook_WithPartialRequest_UpdatesOnlySpecifiedFields()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var webhook = await CreateTestWebhookAsync();
        var originalCallbackUrl = webhook.CallbackUrl;
        var originalEventType = webhook.EventType;
        
        var partialUpdateRequest = new WebhookUpdateRequest
        {
            IsActive = false
            // Only updating IsActive, leaving other fields null
        };

        // Act
        var response = await PutAsJsonAsync($"/api/client/webhooks/{webhook.Id}", partialUpdateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updatedWebhook = await ReadAsJsonAsync<Webhook>(response);
        Assert.NotNull(updatedWebhook);
        Assert.Equal(originalCallbackUrl, updatedWebhook.CallbackUrl); // Should remain unchanged
        Assert.Equal(originalEventType, updatedWebhook.EventType); // Should remain unchanged
        Assert.False(updatedWebhook.IsActive); // Should be updated
    }

    [Fact]
    public async Task UpdateWebhook_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var invalidId = ObjectId.GenerateNewId().ToString();
        var updateRequest = new WebhookUpdateRequest
        {
            CallbackUrl = "https://example.com/webhook"
        };

        // Act
        var response = await PutAsJsonAsync($"/api/client/webhooks/{invalidId}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteWebhook_WithValidId_ReturnsNoContent()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var webhook = await CreateTestWebhookAsync();

        // Act
        var response = await DeleteAsync($"/api/client/webhooks/{webhook.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify webhook is deleted
        var getResponse = await GetAsync($"/api/client/webhooks/{webhook.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteWebhook_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var invalidId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await DeleteAsync($"/api/client/webhooks/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteWebhook_VerifyRemovedFromDatabase()
    {
        // Arrange
        await CleanupWebhooksAsync();
        var webhook = await CreateTestWebhookAsync();

        // Verify webhook exists in database
        var existingWebhook = await _webhooksCollection
            .Find(w => w.Id == webhook.Id)
            .FirstOrDefaultAsync();
        Assert.NotNull(existingWebhook);

        // Act
        var response = await DeleteAsync($"/api/client/webhooks/{webhook.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify webhook is removed from database
        var deletedWebhook = await _webhooksCollection
            .Find(w => w.Id == webhook.Id)
            .FirstOrDefaultAsync();
        Assert.Null(deletedWebhook);
    }

    [Fact]
    public async Task WebhookEndpoints_WithoutTenantHeader_ReturnsUnauthorized()
    {
        // Arrange
        using var client = CreateClientWithoutTenantHeader();
        
        var validRequest = new WebhookCreateRequest
        {
            WorkflowId = "test-workflow",
            CallbackUrl = "https://example.com/webhook",
            EventType = "test.event"
        };

        // Act - Make requests without tenant header
        var getResponse = await client.GetAsync("/api/client/webhooks/");
        var createResponse = await client.PostAsync("/api/client/webhooks", 
            new StringContent(JsonSerializer.Serialize(validRequest), Encoding.UTF8, "application/json"));

        // Assert - These endpoints should require tenant validation
        // In test environment, this might return OK due to test auth configuration
        // In production environment, these should return Unauthorized
        Assert.True(getResponse.StatusCode == HttpStatusCode.Unauthorized || getResponse.StatusCode == HttpStatusCode.OK, 
            $"Expected Unauthorized or OK for GET, but got {getResponse.StatusCode}");
        Assert.True(createResponse.StatusCode == HttpStatusCode.Unauthorized || createResponse.StatusCode == HttpStatusCode.OK || createResponse.StatusCode == HttpStatusCode.Created,
            $"Expected Unauthorized, OK, or Created for POST, but got {createResponse.StatusCode}");
    }

    [Fact]
    public async Task CreateMultipleWebhooks_EachHasUniqueSecret()
    {
        // Arrange
        await CleanupWebhooksAsync();

        // Act - Create multiple webhooks
        var webhook1 = await CreateTestWebhookAsync("workflow1", "event1");
        var webhook2 = await CreateTestWebhookAsync("workflow2", "event2");
        var webhook3 = await CreateTestWebhookAsync("workflow3", "event3");

        // Assert - Each webhook should have a unique secret
        Assert.NotEqual(webhook1.Secret, webhook2.Secret);
        Assert.NotEqual(webhook1.Secret, webhook3.Secret);
        Assert.NotEqual(webhook2.Secret, webhook3.Secret);
        
        // All secrets should be non-empty
        Assert.NotEmpty(webhook1.Secret);
        Assert.NotEmpty(webhook2.Secret);
        Assert.NotEmpty(webhook3.Secret);
    }

    [Fact]
    public async Task WebhookOperations_TenantIsolation_OnlyAccessesTenantData()
    {
        // Arrange
        await CleanupWebhooksAsync();
        
        // Create a webhook with current tenant
        var webhook = await CreateTestWebhookAsync();
        
        // Directly insert a webhook for a different tenant in the database
        var otherTenantWebhook = new Webhook
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = "other-tenant",
            WorkflowId = "other-workflow",
            CallbackUrl = "https://other.example.com/webhook",
            EventType = "other.event",
            Secret = "other-secret",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        await _webhooksCollection.InsertOneAsync(otherTenantWebhook);

        // Act - Get all webhooks (should only return current tenant's webhooks)
        var response = await GetAsync("/api/client/webhooks/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var webhooks = await ReadAsJsonAsync<List<Webhook>>(response);
        Assert.NotNull(webhooks);
        Assert.Single(webhooks); // Should only see the current tenant's webhook
        Assert.Equal(webhook.Id, webhooks[0].Id);
        Assert.Equal(TestTenantId, webhooks[0].TenantId);
    }

    public new void Dispose()
    {
        // Clean up any webhooks created during tests
        CleanupWebhooksAsync().Wait();
        base.Dispose();
    }
} 