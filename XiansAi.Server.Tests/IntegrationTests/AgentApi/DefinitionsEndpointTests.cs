using System.Net;
using System.Net.Http.Json;
using XiansAi.Server.Tests.TestUtils;
using Features.AgentApi.Services.Lib;
using Microsoft.Extensions.DependencyInjection;
using XiansAi.Server.Utils;
using MongoDB.Driver;
using Shared.Data.Models;

namespace XiansAi.Server.Tests.IntegrationTests.AgentApi;

public class DefinitionsEndpointTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    /*
    dotnet test --filter "FullyQualifiedName~DefinitionsEndpointTests"
    */
    public DefinitionsEndpointTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.DefinitionsEndpointTests.CreateDefinition_WithValidData_ReturnsOk"
    */
    [Fact]
    public async Task CreateDefinition_WithValidData_ReturnsOk()
    {
        // Arrange
        string uniqueTypeName = $"test-flow-type-{Guid.NewGuid()}";
        var request = new FlowDefinitionRequest
        {
            WorkflowType = uniqueTypeName,
            Agent = "Test Agent",
            Source = "Example source code",
            ActivityDefinitions = new List<ActivityDefinitionRequest>
            {
                new ActivityDefinitionRequest
                {
                    ActivityName = "TestActivity",
                    AgentToolNames = new List<string> { "tool1", "tool2" },
                    KnowledgeIds = new List<string> { "instruction1" },
                    ParameterDefinitions = new List<ParameterDefinition>
                    {
                        new ParameterDefinition
                        {
                            Name = "param1",
                            Type = "string"
                        }
                    }
                }
            },
            ParameterDefinitions = new List<ParameterDefinitionRequest>
            {
                new ParameterDefinitionRequest
                {
                    Name = "flowParam1",
                    Type = "string"
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/definitions", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("definition created successfully", responseContent);
        
        // Wait for background tasks to complete
        var backgroundTaskService = _factory.Services.GetRequiredService<IBackgroundTaskService>();
        await backgroundTaskService.WaitForCompletionAsync(TimeSpan.FromSeconds(5));
    }

    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.DefinitionsEndpointTests.CreateDefinition_WithInvalidData_ReturnsBadRequest"
    */
    [Fact]
    public async Task CreateDefinition_WithInvalidData_ReturnsBadRequest()
    {
        // Arrange - missing required fields
        var invalidRequest = new
        {
            Agent = "Test Agent",
            Source = "Example source code"
            // Missing required fields: WorkflowType, ActivityDefinitions, ParameterDefinitions
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/definitions", invalidRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.DefinitionsEndpointTests.CreateDefinition_VerifyDataInsertedIntoMongoDB"
    */
    [Fact]
    public async Task CreateDefinition_VerifyDataInsertedIntoMongoDB()
    {
        // Arrange
        string uniqueTypeName = $"test-flow-type-{Guid.NewGuid()}";
        var request = new FlowDefinitionRequest
        {
            WorkflowType = uniqueTypeName,
            Agent = "Test Agent",
            Source = "Example source code",
            ActivityDefinitions = new List<ActivityDefinitionRequest>
            {
                new ActivityDefinitionRequest
                {
                    ActivityName = "TestActivity",
                    AgentToolNames = new List<string> { "tool1", "tool2" },
                    KnowledgeIds = new List<string> { "instruction1" },
                    ParameterDefinitions = new List<ParameterDefinition>
                    {
                        new ParameterDefinition
                        {
                            Name = "param1",
                            Type = "string"
                        }
                    }
                }
            },
            ParameterDefinitions = new List<ParameterDefinitionRequest>
            {
                new ParameterDefinitionRequest
                {
                    Name = "flowParam1",
                    Type = "string"
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/definitions", request);
        
        // Assert HTTP response
        response.EnsureSuccessStatusCode();
        
        // Wait for background tasks to complete
        var backgroundTaskService = _factory.Services.GetRequiredService<IBackgroundTaskService>();
        await backgroundTaskService.WaitForCompletionAsync(TimeSpan.FromSeconds(5));
        
        // Get MongoDB collection and verify data was inserted
        var collection = _database.GetCollection<FlowDefinition>("flow_definitions");
        var filter = Builders<FlowDefinition>.Filter.Eq("workflow_type", uniqueTypeName);
        
        // Allow a few retries as there might be a slight delay
        FlowDefinition? result = null;
        for (int i = 0; i < 3; i++)
        {
            result = await collection.Find(filter).FirstOrDefaultAsync();
            if (result != null) break;
            await Task.Delay(500); // Short delay between retries
        }
        
        // Assert data was inserted correctly
        Assert.NotNull(result);
        Assert.Equal(uniqueTypeName, result?.WorkflowType);
        Assert.Equal("Test Agent", result?.Agent);
        Assert.Equal("Example source code", result?.Source);
        Assert.Single(result!.ActivityDefinitions);
        Assert.NotNull(result?.ActivityDefinitions[0]);
        Assert.Equal("TestActivity", result?.ActivityDefinitions[0].ActivityName);
        Assert.NotNull(result?.ActivityDefinitions[0].AgentToolNames);
        Assert.Contains("tool1", result?.ActivityDefinitions[0].AgentToolNames!);
        Assert.Contains("tool2", result?.ActivityDefinitions[0].AgentToolNames!);
        Assert.Single(result!.ActivityDefinitions[0].KnowledgeIds);
        Assert.Contains("instruction1", result!.ActivityDefinitions[0].KnowledgeIds);
        Assert.Single(result!.ParameterDefinitions);
        Assert.Equal("flowParam1", result?.ParameterDefinitions[0].Name);
        Assert.Equal("string", result?.ParameterDefinitions[0].Type);
    }
    
    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.DefinitionsEndpointTests.CreateDefinition_WithDuplicateTypeName_UpdatesDefinition"
    */
    [Fact]
    public async Task CreateDefinition_WithDuplicateTypeName_UpdatesDefinition()
    {
        // Arrange
        string uniqueTypeName = $"test-flow-type-{Guid.NewGuid()}";
        
        // First request
        var request1 = new FlowDefinitionRequest
        {
            WorkflowType = uniqueTypeName,
            Agent = "Test Agent",
            Source = "Example source code",
            ActivityDefinitions = new List<ActivityDefinitionRequest>
            {
                new ActivityDefinitionRequest
                {
                    ActivityName = "TestActivity",
                    AgentToolNames = new List<string> { "tool1" },
                    KnowledgeIds = new List<string> { "instruction1" },
                    ParameterDefinitions = new List<ParameterDefinition>
                    {
                        new ParameterDefinition { Name = "param1", Type = "string" }
                    }
                }
            },
            ParameterDefinitions = new List<ParameterDefinitionRequest>
            {
                new ParameterDefinitionRequest { Name = "flowParam1", Type = "string" }
            }
        };
        
        // Second request with same TypeName but modified content
        var request2 = new FlowDefinitionRequest
        {
            WorkflowType = uniqueTypeName,
            Agent = "Updated Test Agent",
            Source = "Updated example source code",
            ActivityDefinitions = new List<ActivityDefinitionRequest>
            {
                new ActivityDefinitionRequest
                {
                    ActivityName = "UpdatedTestActivity",
                    AgentToolNames = new List<string> { "tool1", "tool2", "tool3" },
                    KnowledgeIds = new List<string> { "instruction1", "instruction2" },
                    ParameterDefinitions = new List<ParameterDefinition>
                    {
                        new ParameterDefinition { Name = "param1", Type = "string" },
                        new ParameterDefinition { Name = "param2", Type = "number" }
                    }
                }
            },
            ParameterDefinitions = new List<ParameterDefinitionRequest>
            {
                new ParameterDefinitionRequest { Name = "flowParam1", Type = "string" },
                new ParameterDefinitionRequest { Name = "flowParam2", Type = "boolean" }
            }
        };

        // Act
        var response1 = await _client.PostAsJsonAsync("/api/agent/definitions", request1);
        response1.EnsureSuccessStatusCode();
        
        // Wait for background tasks to complete
        var backgroundTaskService = _factory.Services.GetRequiredService<IBackgroundTaskService>();
        await backgroundTaskService.WaitForCompletionAsync(TimeSpan.FromSeconds(5));
        
        // Send the second request
        var response2 = await _client.PostAsJsonAsync("/api/agent/definitions", request2);
        
        // Assert
        response2.EnsureSuccessStatusCode();
        var responseContent = await response2.Content.ReadAsStringAsync();
        Assert.Contains("Definition updated successfully", responseContent);
        
        // Wait for background tasks
        await backgroundTaskService.WaitForCompletionAsync(TimeSpan.FromSeconds(5));
        
        // Get MongoDB collection and verify updated data
        var collection = _database.GetCollection<FlowDefinition>("flow_definitions");
        var filter = Builders<FlowDefinition>.Filter.Eq("workflow_type", uniqueTypeName);
        var result = await collection.Find(filter).SortByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
        
        // Assert updated data was inserted correctly
        Assert.NotNull(result);
        Assert.Equal("Updated Test Agent", result?.Agent);
        Assert.Equal("Updated example source code", result?.Source);
        Assert.Single(result!.ActivityDefinitions);
        Assert.Equal("UpdatedTestActivity", result?.ActivityDefinitions[0].ActivityName);
        Assert.NotNull(result?.ActivityDefinitions[0].AgentToolNames);
        Assert.Equal(3, result?.ActivityDefinitions[0].AgentToolNames!.Count);
        Assert.Equal(2, result?.ActivityDefinitions[0].KnowledgeIds.Count);
        Assert.Equal(2, result?.ParameterDefinitions.Count);
    }

    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.DefinitionsEndpointTests.CreateDefinition_WithSameDefinitionTwice_ReturnsUpToDate"
    */
    [Fact]
    public async Task CreateDefinition_WithSameDefinitionTwice_ReturnsUpToDate()
    {
        // Arrange
        string uniqueTypeName = $"test-flow-type-{Guid.NewGuid()}";
        var request = new FlowDefinitionRequest
        {
            WorkflowType = uniqueTypeName,
            Agent = "Test Agent",   
            Source = "Example source code",
            ActivityDefinitions = new List<ActivityDefinitionRequest>
            {
                new ActivityDefinitionRequest
                {
                    ActivityName = "TestActivity",
                    AgentToolNames = new List<string> { "tool1" },
                    KnowledgeIds = new List<string> { "instruction1" },
                    ParameterDefinitions = new List<ParameterDefinition>
                    {
                        new ParameterDefinition { Name = "param1", Type = "string" }
                    }
                }
            },
            ParameterDefinitions = new List<ParameterDefinitionRequest>
            {
                new ParameterDefinitionRequest { Name = "flowParam1", Type = "string" }
            }
        };

        // Act - First request
        var response1 = await _client.PostAsJsonAsync("/api/agent/definitions", request);
        response1.EnsureSuccessStatusCode();
        
        // Wait for background tasks to complete
        var backgroundTaskService = _factory.Services.GetRequiredService<IBackgroundTaskService>();
        await backgroundTaskService.WaitForCompletionAsync(TimeSpan.FromSeconds(5));
        
        // Send identical request again
        var response2 = await _client.PostAsJsonAsync("/api/agent/definitions", request);
        
        // Assert
        response2.EnsureSuccessStatusCode();
        var responseContent = await response2.Content.ReadAsStringAsync();
        Assert.Contains("already up to date", responseContent);
    }
} 