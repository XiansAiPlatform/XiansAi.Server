using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Shared.Auth;
using Shared.Data;
using Shared.Data.Models;
using Shared.Data.Models.Usage;
using Shared.Repositories;
using Microsoft.Extensions.Logging;
using Tests.IntegrationTests.WebApi;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Base class for AdminApi integration tests.
/// Provides authentication setup with API keys (sk-Xnai- prefix) and helper methods for creating test data.
/// </summary>
public abstract class AdminApiIntegrationTestBase : WebApiIntegrationTestBase
{
    protected string? _adminApiKey;
    protected string? _adminUserId;
    protected string? _adminTenantId;

    protected AdminApiIntegrationTestBase(MongoDbFixture mongoDbFixture, TemporalFixture? temporalFixture = null)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    /// <summary>
    /// Configures the HTTP client with AdminApi authentication using API keys with sk-Xnai- prefix.
    /// </summary>
    protected override void ConfigureClientWithAuth(HttpClient client)
    {
        try
        {
            if (client == null)
            {
                throw new InvalidOperationException("Client is not initialized");
            }

            // Clear existing headers
            client.DefaultRequestHeaders.Clear();

            // Add AdminApi API key (must start with sk-Xnai-)
            if (!string.IsNullOrEmpty(_adminApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminApiKey);
            }

            // Add tenant header if set
            if (!string.IsNullOrEmpty(_adminTenantId))
            {
                client.DefaultRequestHeaders.Add("X-Tenant-Id", _adminTenantId);
            }

            // Add accept headers
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Add user-agent
            client.DefaultRequestHeaders.Add("User-Agent", "XiansAi.Server.Tests");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to configure client with authentication: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates an API key with sk-Xnai- prefix for a user with admin role.
    /// </summary>
    protected async Task<string> CreateTestApiKeyWithAdminRoleAsync(string tenantId, string userId, string role = SystemRoles.SysAdmin)
    {
        using var scope = _factory.Services.CreateScope();
        var apiKeyRepository = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();

        // Create API key - the repository automatically generates keys with sk-Xnai- prefix
        var (apiKey, apiKeyMeta) = await apiKeyRepository.CreateAsync(tenantId, $"test-admin-key-{Guid.NewGuid()}", userId);

        // Ensure the user has the required role
        await CreateTestUserWithRoleAsync(userId, tenantId, role);

        return apiKey;
    }

    /// <summary>
    /// Creates a test user with the specified role (SysAdmin or TenantAdmin).
    /// </summary>
    protected async Task<User> CreateTestUserWithRoleAsync(string userId, string tenantId, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        // Check if user already exists
        var existingUser = await userRepository.GetByUserIdAsync(userId);
        if (existingUser != null)
        {
            // Update existing user with role
            if (role == SystemRoles.SysAdmin)
            {
                existingUser.IsSysAdmin = true;
            }
            else
            {
                // Add tenant role
                var tenantRole = existingUser.TenantRoles.FirstOrDefault(tr => tr.Tenant == tenantId);
                if (tenantRole == null)
                {
                    existingUser.TenantRoles.Add(new TenantRole
                    {
                        Tenant = tenantId,
                        Roles = new List<string> { role },
                        IsApproved = true
                    });
                }
                else if (!tenantRole.Roles.Contains(role))
                {
                    tenantRole.Roles.Add(role);
                }
            }
            await userRepository.UpdateAsync(userId, existingUser);
            return existingUser;
        }

        // Create new user
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = userId,
            Email = $"{userId}@test.com",
            Name = $"Test User {userId}",
            IsSysAdmin = role == SystemRoles.SysAdmin,
            IsLockedOut = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TenantRoles = new List<TenantRole>()
        };

        if (role != SystemRoles.SysAdmin)
        {
            user.TenantRoles.Add(new TenantRole
            {
                Tenant = tenantId,
                Roles = new List<string> { role },
                IsApproved = true
            });
        }

        await userRepository.CreateAsync(user);
        return user;
    }

    /// <summary>
    /// Creates a test tenant.
    /// </summary>
    protected async Task<Tenant> CreateTestTenantAsync(string tenantId, string? name = null, Logo? logo = null)
    {
        using var scope = _factory.Services.CreateScope();
        var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();

        // Check if tenant already exists
        var existingTenant = await tenantRepository.GetByTenantIdAsync(tenantId);
        if (existingTenant != null)
        {
            return existingTenant;
        }

        var tenant = new Tenant
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = tenantId,
            Name = name ?? $"Test Tenant {tenantId}",
            Domain = $"{tenantId}.test.com",
            Description = $"Test tenant for integration tests",
            Logo = logo,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _adminUserId ?? "test-admin",
            Enabled = true
        };

        await tenantRepository.CreateAsync(tenant);
        return tenant;
    }

    /// <summary>
    /// Creates a test agent.
    /// </summary>
    protected async Task<Agent> CreateTestAgentAsync(string agentName, string tenantId, string? userId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        var ownerUserId = userId ?? _adminUserId ?? "test-user";

        // Check if agent already exists
        var existingAgent = await agentRepository.GetByNameAsync(agentName, tenantId, ownerUserId, []);
        if (existingAgent != null)
        {
            return existingAgent;
        }

        var agent = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            Tenant = tenantId,
            OwnerAccess = new List<string> { ownerUserId },
            ReadAccess = new List<string> { ownerUserId },
            WriteAccess = new List<string> { ownerUserId },
            CreatedBy = ownerUserId,
            CreatedAt = DateTime.UtcNow,
            SystemScoped = false
        };

        await agentRepository.CreateAsync(agent);
        return agent;
    }

    /// <summary>
    /// Creates a test knowledge item.
    /// </summary>
    protected async Task<Knowledge> CreateTestKnowledgeAsync(
        string name,
        string agentName,
        string content,
        string tenantId,
        string type = "text",
        DateTime? createdAt = null,
        string? activationName = null)
    {
        using var scope = _factory.Services.CreateScope();
        var knowledgeRepository = scope.ServiceProvider.GetRequiredService<IKnowledgeRepository>();

        var knowledge = new Knowledge
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = name,
            Content = content,
            Type = type,
            Agent = agentName,
            TenantId = tenantId,
            CreatedBy = _adminUserId ?? "test-admin",
            CreatedAt = createdAt ?? DateTime.UtcNow,
            Version = ObjectId.GenerateNewId().ToString(),
            ActivationName = activationName
        };

        await knowledgeRepository.CreateAsync(knowledge);
        return knowledge;
    }

    /// <summary>
    /// Configures the test with an admin API key and user.
    /// Call this method at the start of each test to set up authentication.
    /// </summary>
    protected async Task ConfigureAdminApiClientAsync(string tenantId, string role = SystemRoles.SysAdmin)
    {
        _adminTenantId = tenantId;
        _adminUserId = $"admin-user-{Guid.NewGuid()}";

        // Create user with admin role
        await CreateTestUserWithRoleAsync(_adminUserId, tenantId, role);

        // Create API key for the admin user
        _adminApiKey = await CreateTestApiKeyWithAdminRoleAsync(tenantId, _adminUserId, role);

        // Reconfigure client with the new API key
        ConfigureClientWithAuth(_client.HttpClient);
    }

    /// <summary>
    /// Helper method for PATCH requests (used by AdminApi endpoints).
    /// </summary>
    protected async Task<HttpResponseMessage> PatchAsJsonAsync<T>(string requestUri, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Patch, requestUri)
        {
            Content = content
        };

        return await _client.SendAsync(request);
    }

    /// <summary>
    /// Helper method for DELETE requests with JSON body.
    /// </summary>
    protected async Task<HttpResponseMessage> DeleteAsJsonAsync<T>(string requestUri, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Delete, requestUri)
        {
            Content = content
        };

        return await _client.SendAsync(request);
    }

    /// <summary>
    /// Creates a test agent activation.
    /// </summary>
    protected async Task<AgentActivation> CreateTestActivationAsync(
        string agentName,
        string tenantId,
        string? userId = null,
        bool isActive = false,
        string? name = null)
    {
        using var scope = _factory.Services.CreateScope();
        var activationRepository = scope.ServiceProvider.GetRequiredService<IActivationRepository>();

        var ownerUserId = userId ?? _adminUserId ?? "test-user";

        var activation = new AgentActivation
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = name ?? $"test-activation-{Guid.NewGuid()}",
            AgentName = agentName,
            TenantId = tenantId,
            CreatedBy = ownerUserId,
            CreatedAt = DateTime.UtcNow,
            WorkflowIds = new List<string>(),
            Active = isActive ? true : null,
            ActivatedAt = isActive ? DateTime.UtcNow : null
        };

        await activationRepository.CreateAsync(activation);
        return activation;
    }

    protected async Task<FlowDefinition> CreateBuiltInFlowDefinitionAsync(
        string agentName,
        string tenantId,
        string flowName = "Chat")
    {
        using var scope = _factory.Services.CreateScope();
        var flowDefinitionRepository = scope.ServiceProvider.GetRequiredService<IFlowDefinitionRepository>();

        var definition = new FlowDefinition
        {
            Id = ObjectId.GenerateNewId().ToString(),
            WorkflowType = $"{agentName}:{flowName}",
            Agent = agentName,
            Hash = Guid.NewGuid().ToString("N"),
            ActivityDefinitions = new List<ActivityDefinition>(),
            ParameterDefinitions = new List<ParameterDefinition>(),
            CreatedBy = _adminUserId ?? "test-admin",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Tenant = tenantId,
            SystemScoped = false,
            Activable = true,
            IsBuiltIn = true
        };

        await flowDefinitionRepository.CreateAsync(definition);
        return definition;
    }

    protected async Task<(string ThreadId, string WorkflowId, string WorkflowType, ConversationMessage Message)> SeedConversationAsync(
        string tenantId,
        string agentName,
        string activationName,
        string participantId,
        string text = "hello from admin tests",
        string? topic = null,
        MessageDirection direction = MessageDirection.Incoming)
    {
        var workflowType = $"{agentName}:Chat";
        var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, "Chat", activationName);
        var normalizedParticipant = participantId.ToLowerInvariant();

        using var scope = _factory.Services.CreateScope();
        var conversationRepository = scope.ServiceProvider.GetRequiredService<IConversationRepository>();

        var threadId = await conversationRepository.CreateOrGetThreadIdAsync(new ConversationThread
        {
            TenantId = tenantId,
            WorkflowId = workflowId,
            WorkflowType = workflowType,
            Agent = agentName,
            ParticipantId = normalizedParticipant,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = _adminUserId ?? "test-admin",
            Status = ConversationThreadStatus.Active
        });

        var message = new ConversationMessage
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ThreadId = threadId,
            TenantId = tenantId,
            ParticipantId = normalizedParticipant,
            WorkflowId = workflowId,
            WorkflowType = workflowType,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _adminUserId ?? "test-admin",
            Direction = direction,
            Text = text,
            Status = MessageStatus.Read,
            Scope = topic,
            MessageType = MessageType.Chat
        };
        await conversationRepository.SaveMessageAsync(message);

        return (threadId, workflowId, workflowType, message);
    }

    protected async Task<AuditLogEntry> SeedAuditLogAsync(
        string tenantId,
        string action,
        string performedBy,
        string? activationName = null)
    {
        using var scope = _factory.Services.CreateScope();
        var auditLogRepository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var entry = new AuditLogEntry
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = tenantId,
            ParticipantId = performedBy,
            LoggedInUser = performedBy,
            Action = action,
            Description = $"{action} performed in tests",
            ActivationName = activationName,
            CreatedAt = DateTime.UtcNow
        };

        await auditLogRepository.CreateAsync(entry);
        return entry;
    }

    protected async Task SeedLogAsync(
        string tenantId,
        string agentName,
        string activationName,
        string workflowId,
        string message = "admin integration log")
    {
        using var scope = _factory.Services.CreateScope();
        var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var mongoDatabase = await databaseService.GetDatabaseAsync();
        var collection = mongoDatabase.GetCollection<Features.WebApi.Models.Log>("logs");

        await collection.InsertOneAsync(new Features.WebApi.Models.Log
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = tenantId,
            CreatedAt = DateTime.UtcNow,
            Level = LogLevel.Information,
            Message = message,
            WorkflowId = workflowId,
            WorkflowRunId = ObjectId.GenerateNewId().ToString(),
            WorkflowType = $"{agentName}:Chat",
            Agent = agentName,
            Activation = activationName,
            ParticipantId = "user@example.com"
        });
    }

    protected async Task SeedUsageMetricAsync(
        string tenantId,
        string agentName,
        string activationName,
        string category = "llm",
        string type = "tokens",
        double value = 42)
    {
        using var scope = _factory.Services.CreateScope();
        var usageRepository = scope.ServiceProvider.GetRequiredService<IUsageEventRepository>();

        await usageRepository.InsertBatchAsync(
        [
            new UsageMetric
            {
                TenantId = tenantId,
                AgentName = agentName,
                ActivationName = activationName,
                ParticipantId = "user@example.com",
                Category = category,
                Type = type,
                Value = value,
                Unit = "count",
                CreatedAt = DateTime.UtcNow
            }
        ]);
    }

    /// <summary>
    /// Deletes a test activation.
    /// </summary>
    protected async Task DeleteTestActivationAsync(string activationId)
    {
        using var scope = _factory.Services.CreateScope();
        var activationRepository = scope.ServiceProvider.GetRequiredService<IActivationRepository>();
        await activationRepository.DeleteAsync(activationId);
    }
}
