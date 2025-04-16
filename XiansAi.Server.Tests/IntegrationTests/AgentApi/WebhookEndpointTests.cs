using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XiansAi.Server.Tests.TestUtils;
using XiansAi.Server.Features.AgentApi.Models;
using XiansAi.Server.Shared.Data.Models;
using MongoDB.Driver;
using MongoDB.Bson;

namespace XiansAi.Server.Tests.IntegrationTests.AgentApi;

public class WebhookEndpointTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    /*
    dotnet test --filter "FullyQualifiedName~WebhookEndpointTests"    
    */
    public WebhookEndpointTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    /*
    dotnet test --filter "FullyQualifiedName~WebhookEndpointTests.RegisterWebhook_WithValidData_ReturnsOk"
    */
    [Fact]
    public async Task RegisterWebhook_WithValidData_ReturnsOk()
    {
        // Arrange
        var request = new WebhookRegistrationDto
        {
            WorkflowId = "test-workflow-id",
            CallbackUrl = "https://example.com/webhook",
            EventType = "test.event"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/webhooks/register", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var webhook = await response.Content.ReadFromJsonAsync<Webhook>();
        Assert.NotNull(webhook);
        Assert.Equal(request.WorkflowId, webhook.WorkflowId);
        Assert.Equal(request.CallbackUrl, webhook.CallbackUrl);
        Assert.Equal(request.EventType, webhook.EventType);
        Assert.True(webhook.IsActive);
        Assert.NotNull(webhook.Secret);
    }

    [Fact]
    public async Task RegisterWebhook_WithInvalidData_ReturnsBadRequest()
    {
        // Arrange - missing required fields
        var invalidRequest = new
        {
            WorkflowId = "test-workflow-id"
            // Missing required fields: CallbackUrl, EventType
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/webhooks/register", invalidRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetWebhook_WithValidId_ReturnsWebhook()
    {
        // Arrange - Create a webhook first
        var registrationRequest = new WebhookRegistrationDto
        {
            WorkflowId = "test-workflow-id",
            CallbackUrl = "https://example.com/webhook",
            EventType = "test.event"
        };
        var registrationResponse = await _client.PostAsJsonAsync("/api/agent/webhooks/register", registrationRequest);
        registrationResponse.EnsureSuccessStatusCode();
        var webhook = await registrationResponse.Content.ReadFromJsonAsync<Webhook>();
        Assert.NotNull(webhook);

        // Act - Get the webhook
        var response = await _client.GetAsync($"/api/agent/webhooks/{webhook.Id}");

        // Assert
        response.EnsureSuccessStatusCode();
        var retrievedWebhook = await response.Content.ReadFromJsonAsync<Webhook>();
        Assert.NotNull(retrievedWebhook);
        Assert.Equal(webhook.Id, retrievedWebhook.Id);
        Assert.Equal(webhook.WorkflowId, retrievedWebhook.WorkflowId);
        Assert.Equal(webhook.CallbackUrl, retrievedWebhook.CallbackUrl);
        Assert.Equal(webhook.EventType, retrievedWebhook.EventType);
    }

    [Fact]
    public async Task GetWebhook_WithInvalidId_ReturnsNotFound()
    {
        // Arrange - Use a non-existent webhook ID
        var invalidId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await _client.GetAsync($"/api/agent/webhooks/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteWebhook_WithValidId_ReturnsOk()
    {
        // Arrange - Create a webhook first
        var registrationRequest = new WebhookRegistrationDto
        {
            WorkflowId = "test-workflow-id",
            CallbackUrl = "https://example.com/webhook",
            EventType = "test.event"
        };
        var registrationResponse = await _client.PostAsJsonAsync("/api/agent/webhooks/register", registrationRequest);
        registrationResponse.EnsureSuccessStatusCode();
        var webhook = await registrationResponse.Content.ReadFromJsonAsync<Webhook>();
        Assert.NotNull(webhook);
        // Act - Delete the webhook
        var response = await _client.DeleteAsync($"/api/agent/webhooks/{webhook.Id}");

        // Assert
        response.EnsureSuccessStatusCode();

        // Verify webhook is deleted
        var getResponse = await _client.GetAsync($"/api/agent/webhooks/{webhook.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteWebhook_WithInvalidId_ReturnsNotFound()
    {
        // Arrange - Use a non-existent webhook ID
        var invalidId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await _client.DeleteAsync($"/api/agent/webhooks/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TriggerWebhook_WithValidData_ReturnsOk()
    {
        // Arrange - Create a webhook first
        var registrationRequest = new WebhookRegistrationDto
        {
            WorkflowId = "test-workflow-id",
            CallbackUrl = "https://example.com/webhook",
            EventType = "test.event"
        };
        var registrationResponse = await _client.PostAsJsonAsync("/api/agent/webhooks/register", registrationRequest);
        registrationResponse.EnsureSuccessStatusCode();

        // Create a trigger request
        var triggerRequest = new WebhookTriggerDto
        {
            WorkflowId = "test-workflow-id",
            EventType = "test.event",
            Payload = new { test = "data" }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/webhooks/trigger", triggerRequest);

        // Log response details for debugging
        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response Status: {response.StatusCode}");
        Console.WriteLine($"Response Content: {responseContent}");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.True(result.GetProperty("webhooksTriggered").GetInt32() > 0);
    }

    [Fact]
    public async Task TriggerWebhook_WithInvalidData_ReturnsBadRequest()
    {
        // Arrange - missing required fields
        var invalidRequest = new
        {
            WorkflowId = "test-workflow-id"
            // Missing required fields: EventType, Payload
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/webhooks/trigger", invalidRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TriggerWebhook_WithNonExistentWorkflow_ReturnsBadRequest()
    {
        // Arrange
        var triggerRequest = new WebhookTriggerDto
        {
            WorkflowId = "non-existent-workflow-id",
            EventType = "test.event",
            Payload = new { test = "data" }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/webhooks/trigger", triggerRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterWebhook_VerifyDataInsertedIntoMongoDB()
    {
        // Arrange
        string uniqueWorkflowId = $"test-workflow-id-{Guid.NewGuid()}";
        var request = new WebhookRegistrationDto
        {
            WorkflowId = uniqueWorkflowId,
            CallbackUrl = "https://example.com/webhook",
            EventType = "test.event"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/webhooks/register", request);
        
        // Assert HTTP response
        response.EnsureSuccessStatusCode();
        
        // Get MongoDB collection and verify data was inserted
        var collection = _database.GetCollection<Webhook>("webhooks");
        var filter = Builders<Webhook>.Filter.Eq("WorkflowId", uniqueWorkflowId);
        
        // Allow a few retries as there might be a slight delay
        Webhook? result = null;
        for (int i = 0; i < 3; i++)
        {
            result = await collection.Find(filter).FirstOrDefaultAsync();
            if (result != null) break;
            await Task.Delay(500); // Short delay between retries
        }
        
        // Assert data was inserted correctly
        Assert.NotNull(result);
        Assert.Equal(uniqueWorkflowId, result.WorkflowId);
        Assert.Equal("https://example.com/webhook", result.CallbackUrl);
        Assert.Equal("test.event", result.EventType);
        Assert.True(result.IsActive);
        Assert.NotNull(result.Secret);
    }
} 