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
            TypeName = uniqueTypeName,
            AgentName = "Test Agent",
            Source = "Example source code",
            Activities = new List<ActivityDefinition>
            {
                new ActivityDefinition
                {
                    ActivityName = "TestActivity",
                    AgentToolNames = new List<string> { "tool1", "tool2" },
                    Instructions = new List<string> { "instruction1" },
                    Parameters = new List<ParameterDefinition>
                    {
                        new ParameterDefinition
                        {
                            Name = "param1",
                            Type = "string"
                        }
                    }
                }
            },
            Parameters = new List<ParameterRequest>
            {
                new ParameterRequest
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
            AgentName = "Test Agent",
            Source = "Example source code"
            // Missing required fields: TypeName, Activities, Parameters
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
            TypeName = uniqueTypeName,
            AgentName = "Test Agent",
            Source = "Example source code",
            Activities = new List<ActivityDefinition>
            {
                new ActivityDefinition
                {
                    ActivityName = "TestActivity",
                    AgentToolNames = new List<string> { "tool1", "tool2" },
                    Instructions = new List<string> { "instruction1" },
                    Parameters = new List<ParameterDefinition>
                    {
                        new ParameterDefinition
                        {
                            Name = "param1",
                            Type = "string"
                        }
                    }
                }
            },
            Parameters = new List<ParameterRequest>
            {
                new ParameterRequest
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
        var collection = _database.GetCollection<FlowDefinition>("definitions");
        var filter = Builders<FlowDefinition>.Filter.Eq("type_name", uniqueTypeName);
        
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
        Assert.Equal(uniqueTypeName, result?.TypeName);
        Assert.Equal("Test Agent", result?.AgentName);
        Assert.Equal("Example source code", result?.Source);
        Assert.Single(result!.Activities);
        Assert.NotNull(result?.Activities[0]);
        Assert.Equal("TestActivity", result?.Activities[0].ActivityName);
        Assert.NotNull(result?.Activities[0].AgentToolNames);
        Assert.Contains("tool1", result?.Activities[0].AgentToolNames!);
        Assert.Contains("tool2", result?.Activities[0].AgentToolNames!);
        Assert.Single(result!.Activities[0].Instructions);
        Assert.Contains("instruction1", result!.Activities[0].Instructions);
        Assert.Single(result!.Parameters);
        Assert.Equal("flowParam1", result?.Parameters[0].Name);
        Assert.Equal("string", result?.Parameters[0].Type);
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
            TypeName = uniqueTypeName,
            AgentName = "Test Agent",
            Source = "Example source code",
            Activities = new List<ActivityDefinition>
            {
                new ActivityDefinition
                {
                    ActivityName = "TestActivity",
                    AgentToolNames = new List<string> { "tool1" },
                    Instructions = new List<string> { "instruction1" },
                    Parameters = new List<ParameterDefinition>
                    {
                        new ParameterDefinition { Name = "param1", Type = "string" }
                    }
                }
            },
            Parameters = new List<ParameterRequest>
            {
                new ParameterRequest { Name = "flowParam1", Type = "string" }
            }
        };
        
        // Second request with same TypeName but modified content
        var request2 = new FlowDefinitionRequest
        {
            TypeName = uniqueTypeName,
            AgentName = "Updated Test Agent",
            Source = "Updated example source code",
            Activities = new List<ActivityDefinition>
            {
                new ActivityDefinition
                {
                    ActivityName = "UpdatedTestActivity",
                    AgentToolNames = new List<string> { "tool1", "tool2", "tool3" },
                    Instructions = new List<string> { "instruction1", "instruction2" },
                    Parameters = new List<ParameterDefinition>
                    {
                        new ParameterDefinition { Name = "param1", Type = "string" },
                        new ParameterDefinition { Name = "param2", Type = "number" }
                    }
                }
            },
            Parameters = new List<ParameterRequest>
            {
                new ParameterRequest { Name = "flowParam1", Type = "string" },
                new ParameterRequest { Name = "flowParam2", Type = "boolean" }
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
        Assert.Contains("different hash", responseContent);
        
        // Wait for background tasks
        await backgroundTaskService.WaitForCompletionAsync(TimeSpan.FromSeconds(5));
        
        // Get MongoDB collection and verify updated data
        var collection = _database.GetCollection<FlowDefinition>("definitions");
        var filter = Builders<FlowDefinition>.Filter.Eq("type_name", uniqueTypeName);
        var result = await collection.Find(filter).SortByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
        
        // Assert updated data was inserted correctly
        Assert.NotNull(result);
        Assert.Equal("Updated Test Agent", result?.AgentName);
        Assert.Equal("Updated example source code", result?.Source);
        Assert.Single(result!.Activities);
        Assert.Equal("UpdatedTestActivity", result?.Activities[0].ActivityName);
        Assert.NotNull(result?.Activities[0].AgentToolNames);
        Assert.Equal(3, result?.Activities[0].AgentToolNames!.Count);
        Assert.Equal(2, result?.Activities[0].Instructions.Count);
        Assert.Equal(2, result?.Parameters.Count);
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
            TypeName = uniqueTypeName,
            AgentName = "Test Agent",
            Source = "Example source code",
            Activities = new List<ActivityDefinition>
            {
                new ActivityDefinition
                {
                    ActivityName = "TestActivity",
                    AgentToolNames = new List<string> { "tool1" },
                    Instructions = new List<string> { "instruction1" },
                    Parameters = new List<ParameterDefinition>
                    {
                        new ParameterDefinition { Name = "param1", Type = "string" }
                    }
                }
            },
            Parameters = new List<ParameterRequest>
            {
                new ParameterRequest { Name = "flowParam1", Type = "string" }
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