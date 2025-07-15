# How to Add Definitions in the API

## Overview

The XiansAi.Server provides two main ways to add definitions:

1. **AgentApi** (`/api/agent/definitions`) - For agent-to-server communication (requires certificate authentication)
2. **WebApi** (planned) - For user interface interactions (requires JWT authentication)

## Current Implementation

### AgentApi Definition Creation

The AgentApi already has a complete definition creation endpoint at `/api/agent/definitions`.

#### Endpoint Details:
- **URL**: `POST /api/agent/definitions`
- **Authentication**: Certificate-based authentication
- **Content-Type**: `application/json`

#### Request Model: `FlowDefinitionRequest`

```json
{
  "agent": "string",                    // Required: Agent name
  "workflowType": "string",             // Required: Unique workflow type identifier
  "source": "string",                   // Optional: Source code or description
  "activityDefinitions": [               // Required: List of activity definitions
    {
      "activityName": "string",         // Required: Name of the activity
      "agentToolNames": ["string"],     // Optional: List of tool names
      "knowledgeIds": ["string"],       // Required: List of knowledge IDs
      "parameterDefinitions": [          // Required: Activity parameters
        {
          "name": "string",             // Required: Parameter name
          "type": "string"              // Required: Parameter type
        }
      ]
    }
  ],
  "parameterDefinitions": [              // Required: Workflow-level parameters
    {
      "name": "string",                 // Required: Parameter name
      "type": "string"                  // Required: Parameter type
    }
  ]
}
```

## API Usage Examples

### 1. Basic Definition Creation

```bash
curl -X POST "http://localhost:5001/api/agent/definitions" \
  -H "Content-Type: application/json" \
  -H "X-Certificate: your-certificate" \
  -d '{
    "agent": "MyAgent",
    "workflowType": "customer-support-workflow",
    "source": "Customer support workflow definition",
    "activityDefinitions": [
      {
        "activityName": "GreetCustomer",
        "agentToolNames": ["greeting_tool", "language_detector"],
        "knowledgeIds": ["greeting_instructions", "language_guide"],
        "parameterDefinitions": [
          {
            "name": "customerName",
            "type": "string"
          },
          {
            "name": "language",
            "type": "string"
          }
        ]
      },
      {
        "activityName": "ProcessInquiry",
        "agentToolNames": ["inquiry_processor", "knowledge_base"],
        "knowledgeIds": ["inquiry_handling", "faq_knowledge"],
        "parameterDefinitions": [
          {
            "name": "inquiryType",
            "type": "string"
          },
          {
            "name": "priority",
            "type": "integer"
          }
        ]
      }
    ],
    "parameterDefinitions": [
      {
        "name": "customerId",
        "type": "string"
      },
      {
        "name": "sessionTimeout",
        "type": "integer"
      }
    ]
  }'
```

### 2. Using JavaScript/Node.js

```javascript
const axios = require('axios');

const definitionRequest = {
  agent: "MyAgent",
  workflowType: "customer-support-workflow",
  source: "Customer support workflow definition",
  activityDefinitions: [
    {
      activityName: "GreetCustomer",
      agentToolNames: ["greeting_tool", "language_detector"],
      knowledgeIds: ["greeting_instructions", "language_guide"],
      parameterDefinitions: [
        { name: "customerName", type: "string" },
        { name: "language", type: "string" }
      ]
    }
  ],
  parameterDefinitions: [
    { name: "customerId", type: "string" },
    { name: "sessionTimeout", type: "integer" }
  ]
};

const response = await axios.post(
  'http://localhost:5001/api/agent/definitions',
  definitionRequest,
  {
    headers: {
      'Content-Type': 'application/json',
      'X-Certificate': 'your-certificate-here'
    }
  }
);

console.log('Definition created:', response.data);
```

### 3. Using Python

```python
import requests
import json

definition_request = {
    "agent": "MyAgent",
    "workflowType": "customer-support-workflow",
    "source": "Customer support workflow definition",
    "activityDefinitions": [
        {
            "activityName": "GreetCustomer",
            "agentToolNames": ["greeting_tool", "language_detector"],
            "knowledgeIds": ["greeting_instructions", "language_guide"],
            "parameterDefinitions": [
                {"name": "customerName", "type": "string"},
                {"name": "language", "type": "string"}
            ]
        }
    ],
    "parameterDefinitions": [
        {"name": "customerId", "type": "string"},
        {"name": "sessionTimeout", "type": "integer"}
    ]
}

response = requests.post(
    'http://localhost:5001/api/agent/definitions',
    json=definition_request,
    headers={
        'Content-Type': 'application/json',
        'X-Certificate': 'your-certificate-here'
    }
)

print('Response:', response.json())
```

## Response Examples

### Success Response (200 OK)
```json
{
  "message": "New definition created successfully"
}
```

### Update Response (200 OK)
```json
{
  "message": "Definition already up to date"
}
```

### Error Response (400 Bad Request)
```json
{
  "message": "Agent name is required."
}
```

### Permission Error (403 Forbidden)
```json
{
  "message": "User `user123` does not have write permission for agent `MyAgent` which is owned by another user. Please use a different name or ask the owner to share the agent with you with write permission."
}
```

## Adding WebApi Definition Endpoints

To add definition creation endpoints to the WebApi (for user interface), you would need to:

### 1. Add to AgentService Interface

```csharp
public interface IAgentService
{
    // ... existing methods ...
    Task<ServiceResult<FlowDefinition>> CreateDefinition(string agentName, FlowDefinitionRequest request);
    Task<ServiceResult<FlowDefinition>> UpdateDefinition(string agentName, string workflowType, FlowDefinitionRequest request);
    Task<ServiceResult<bool>> DeleteDefinition(string agentName, string workflowType);
}
```

### 2. Add to AgentService Implementation

```csharp
public async Task<ServiceResult<FlowDefinition>> CreateDefinition(string agentName, FlowDefinitionRequest request)
{
    try
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(agentName))
            return ServiceResult<FlowDefinition>.BadRequest("Agent name is required");

        // Check permissions
        var writePermissionResult = await _permissionsService.HasWritePermission(agentName);
        if (!writePermissionResult.IsSuccess || !writePermissionResult.Data)
            return ServiceResult<FlowDefinition>.Forbidden("You do not have write permission for this agent");

        // Create definition using existing service
        var definitionsService = new DefinitionsService(
            _definitionRepository, 
            _agentRepository, 
            _logger, 
            _tenantContext,
            _markdownService,
            _agentPermissionRepository
        );

        var result = await definitionsService.CreateAsync(request);
        
        // Convert IResult to ServiceResult
        if (result is Ok)
            return ServiceResult<FlowDefinition>.Success(null); // You'd need to return the actual definition
        else
            return ServiceResult<FlowDefinition>.BadRequest("Failed to create definition");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error creating definition for agent {AgentName}", agentName);
        return ServiceResult<FlowDefinition>.InternalServerError("An error occurred while creating the definition");
    }
}
```

### 3. Add to AgentEndpoints

```csharp
agentGroup.MapPost("/{agentName}/definitions", async (
    string agentName,
    [FromBody] FlowDefinitionRequest request,
    [FromServices] IAgentService service) =>
{
    var result = await service.CreateDefinition(agentName, request);
    return result.ToHttpResult();
})
.WithName("Create Definition")
.WithOpenApi(operation => {
    operation.Summary = "Create a new definition for an agent";
    operation.Description = "Creates a new flow definition for the specified agent. Requires write permission.";
    return operation;
});
```

## Best Practices

### 1. Naming Conventions
- Use descriptive workflow type names: `customer-support-workflow`, `order-processing-workflow`
- Use kebab-case for workflow types and snake_case for activity names
- Make agent names unique within your tenant

### 2. Parameter Types
Supported parameter types:
- `string` - Text values
- `integer` - Whole numbers
- `number` - Decimal numbers
- `boolean` - True/false values
- `object` - Complex objects
- `array` - Lists of values

### 3. Knowledge Integration
- Reference existing knowledge IDs in `knowledgeIds`
- Ensure knowledge exists before creating definitions
- Use descriptive knowledge names

### 4. Tool Integration
- Reference agent tool names in `agentToolNames`
- Ensure tools are available to the agent
- Use consistent tool naming

### 5. Error Handling
- Always check response status codes
- Handle permission errors gracefully
- Validate input data before sending

## Testing Definitions

### 1. Create Test Definition
```bash
curl -X POST "http://localhost:5001/api/agent/definitions" \
  -H "Content-Type: application/json" \
  -H "X-Certificate: test-certificate" \
  -d '{
    "agent": "TestAgent",
    "workflowType": "test-workflow",
    "source": "Test workflow for debugging",
    "activityDefinitions": [
      {
        "activityName": "TestActivity",
        "agentToolNames": ["test_tool"],
        "knowledgeIds": ["test_knowledge"],
        "parameterDefinitions": [
          {"name": "testParam", "type": "string"}
        ]
      }
    ],
    "parameterDefinitions": [
      {"name": "workflowParam", "type": "string"}
    ]
  }'
```

### 2. Verify Definition Creation
```bash
curl -X GET "http://localhost:5001/api/client/agents/TestAgent/definitions/basic" \
  -H "Authorization: Bearer your-jwt-token" \
  -H "X-Tenant-Id: your-tenant-id"
```

## Debugging Definition Creation

### 1. Set Breakpoints
- In `DefinitionsService.CreateAsync()` method
- In `AgentService.CreateDefinition()` method (when implemented)
- In validation logic

### 2. Check Logs
```bash
# Monitor application logs
tail -f logs/application.log | grep "CreateAsync\|CreateDefinition"
```

### 3. Database Verification
```bash
# Check MongoDB for created definitions
mongo xiansai_dev --eval "db.flow_definitions.find({agent: 'TestAgent'}).pretty()"
```

## Common Issues and Solutions

### 1. Permission Denied
**Issue**: 403 Forbidden error
**Solution**: Ensure user has write permission for the agent

### 2. Agent Not Found
**Issue**: Agent doesn't exist
**Solution**: The system will create the agent automatically if it doesn't exist

### 3. Invalid Request Format
**Issue**: 400 Bad Request
**Solution**: Check that all required fields are present and properly formatted

### 4. Duplicate Workflow Type
**Issue**: Definition already exists
**Solution**: The system will update the existing definition if the content has changed

## Next Steps

1. **Implement WebApi endpoints** for user interface integration
2. **Add validation** for workflow type uniqueness
3. **Add bulk operations** for multiple definitions
4. **Add versioning** for definition changes
5. **Add rollback** functionality for definition updates 