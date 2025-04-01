# Client API Postman Test Scripts Guide

This document provides instructions for setting up and creating Postman test scripts for the Client API endpoints.

## File Location

- Postman test scripts should be located in the `Tests/postman` directory
- Test scripts should be named in kebab-case: `<entity>-api-endpoints.postman_collection.json`

## Setup

### Collection Variables

Configure variables at the collection level using the `variable` array in your collection JSON:

```json
{
  "info": {
    "name": "API Endpoints",
    "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  "variable": [
    {
      "key": "baseUrl",
      "value": "http://localhost:5000",
      "type": "string"
    },
    {
      "key": "authToken",
      "value": "your_auth_token_here",
      "type": "string"
    },
    {
      "key": "tenantId",
      "value": "your_tenant_id_here",
      "type": "string"
    }
  ],
  "item": []
}
```

Mandatory variables:

- `baseUrl` : Base URL of the API
- `authToken` : Bearer token in Authorization header
- `tenantId` : X-Tenant-Id header

### Auto-Generating Test Data

Add these pre-request scripts at the collection level to auto-generate test data:

```javascript
// Pre-request Script
if (!pm.collectionVariables.get("testId")) {
    // Generate a unique ID for test resources
    const testId = "test_" + Date.now();
    pm.collectionVariables.set("testId", testId);
}

// Generate other required fields
pm.collectionVariables.set("testName", "Test Resource " + pm.collectionVariables.get("testId"));
pm.collectionVariables.set("testDescription", "Auto-generated test resource");
```

Then use these variables in your request bodies:

```json
{
    "id": "{{testId}}",
    "name": "{{testName}}",
    "description": "{{testDescription}}"
}
```

### Collection Structure

Organize your test collection by resource type (projects, activities, etc.) with tests for each CRUD operation.

## Creating Postman Collections Programmatically

### Collection JSON Format

The following example shows how to structure your Postman collection JSON file with collection-level variables.

Save it in the folder `./postman/collections` with a descriptive name like `{name}-api-endpoints.postman_collection.json`:

```json
{
  "info": {
    "name": "API Endpoints",
    "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  "variable": [
    {
      "key": "baseUrl",
      "value": "http://localhost:5000",
      "type": "string"
    },
    {
      "key": "authToken",
      "value": "",
      "type": "string"
    },
    {
      "key": "tenantId",
      "value": "",
      "type": "string"
    }
  ],
  "item": [
    {
      "name": "Get All Resources",
      "request": {
        "method": "GET",
        "header": [
          {
            "key": "Authorization",
            "value": "Bearer {{authToken}}"
          },
          {
            "key": "X-Tenant-Id",
            "value": "{{tenantId}}"
          }
        ],
        "url": {
          "raw": "{{baseUrl}}/api/client/resources",
          "host": ["{{baseUrl}}"],
          "path": ["api", "client", "resources"]
        }
      },
      "event": [
        {
          "listen": "test",
          "script": {
            "exec": [
              "pm.test(\"Status code is 200\", function () {",
              "    pm.response.to.have.status(200);",
              "});",
              "",
              "pm.test(\"Response structure is valid\", function () {",
              "    const jsonData = pm.response.json();",
              "    pm.expect(jsonData).to.be.an('array');",
              "    if (jsonData.length > 0) {",
              "        pm.expect(jsonData[0]).to.have.property('id');",
              "    }",
              "});"
            ],
            "type": "text/javascript"
          }
        }
      ]
    },
    {
      "name": "Create Resource",
      "request": {
        "method": "POST",
        "header": [
          {
            "key": "Authorization",
            "value": "Bearer {{authToken}}"
          },
          {
            "key": "X-Tenant-Id",
            "value": "{{tenantId}}"
          },
          {
            "key": "Content-Type",
            "value": "application/json"
          }
        ],
        "body": {
          "mode": "raw",
          "raw": "{\n    \"name\": \"Test Resource\",\n    \"description\": \"A test resource\"\n}"
        },
        "url": {
          "raw": "{{baseUrl}}/api/client/resources",
          "host": ["{{baseUrl}}"],
          "path": ["api", "client", "resources"]
        }
      },
      "event": [
        {
          "listen": "test",
          "script": {
            "exec": [
              "pm.test(\"Status code is 200\", function () {",
              "    pm.response.to.have.status(200);",
              "});",
              "",
              "pm.test(\"Save resource ID\", function () {",
              "    const jsonData = pm.response.json();",
              "    pm.expect(jsonData).to.have.property('id');",
              "    pm.environment.set(\"resourceId\", jsonData.id);",
              "});"
            ],
            "type": "text/javascript"
          }
        }
      ]
    }
  ]
}
```

### Using Newman for CLI Testing

To run your collections from the command line:

```bash
# Install Newman
npm install -g newman

# Run the collection
newman run api-endpoints.postman_collection.json
```

Note: When using Newman, you can override collection variables using the `--env-var` option:

```bash
newman run api-endpoints.postman_collection.json --env-var "baseUrl=https://api.example.com"
```

This revised documentation focuses on collection-level variables instead of separate environment files, making it easier to manage and share collections with their associated variables.

### Cleanup Scripts

Add these post-request scripts to clean up test data:

```javascript
// Post-request Script for cleanup after tests
pm.test("Cleanup test data", function() {
    if (pm.response.code === 200) {
        // Clear generated test data
        pm.collectionVariables.unset("testId");
        pm.collectionVariables.unset("testName");
        pm.collectionVariables.unset("testDescription");
    }
});
```
