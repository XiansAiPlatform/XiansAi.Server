# Client API Postman Test Scripts Guide

This document provides instructions for setting up and creating Postman test scripts for the Client API endpoints.

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

### Collection Structure

Organize your test collection by resource type (projects, activities, etc.) with tests for each CRUD operation.

## Creating Test Scripts

### Basic Test Structure

Every API endpoint test should include:

1. **Status Code Verification**

```javascript
pm.test("Status code is 200", function () {
    pm.response.to.have.status(200);
});
```

2. **Response Structure Validation**

```javascript
pm.test("Response has correct structure", function () {
    var jsonData = pm.response.json();
    pm.expect(jsonData).to.be.an('object');
    // Add expected properties
    pm.expect(jsonData).to.have.property('id');
    pm.expect(jsonData).to.have.property('name');
});
```

3. **Business Logic Validation**

```javascript
pm.test("Business logic is correct", function () {
    var jsonData = pm.response.json();
    // Add business-specific validations
    pm.expect(jsonData.status).to.be.oneOf(['PENDING', 'IN_PROGRESS', 'COMPLETED']);
});
```

### Test Template by HTTP Method

#### GET Requests

```javascript
// 1. Status check
pm.test("Status code is 200", function () {
    pm.response.to.have.status(200);
});

// 2. Response format check
pm.test("Response is in JSON format", function () {
    pm.response.to.be.json;
});

// 3. Data validation
pm.test("Data is valid", function () {
    var jsonData = pm.response.json();
    pm.expect(jsonData).to.be.an('object');
    // Add specific field validations
});
```

#### POST Requests

```javascript
// 1. Status check
pm.test("Status code is 200", function () {
    pm.response.to.have.status(200);
});

// 2. Created resource validation
pm.test("Resource created successfully", function () {
    var jsonData = pm.response.json();
    pm.expect(jsonData).to.have.property('id');
    // Validate against request data
    pm.expect(jsonData.name).to.eql(pm.request.body.raw.name);
});

// 3. Store ID for subsequent requests (if needed)
var jsonData = pm.response.json();
pm.collectionVariables.set("resourceId", jsonData.id);
```

#### PUT Requests

```javascript
// 1. Status check
pm.test("Status code is 200", function () {
    pm.response.to.have.status(200);
});

// 2. Update verification
pm.test("Resource updated successfully", function () {
    var jsonData = pm.response.json();
    // Verify updated fields
    pm.expect(jsonData.updatedField).to.eql(newValue);
});
```

#### DELETE Requests

```javascript
// 1. Status check
pm.test("Status code is 204", function () {
    pm.response.to.have.status(204);
});

// 2. Verify deletion (optional - requires subsequent GET)
pm.sendRequest({
    url: pm.variables.get("baseUrl") + "/resource/" + resourceId,
    method: 'GET'
}, function (err, response) {
    pm.test("Resource was deleted", function() {
        pm.expect(response.code).to.eql(404);
    });
});
```

### Error Handling Templates

#### Required Field Validation

```javascript
pm.test("Required fields validation", function () {
    var jsonData = pm.response.json();
    if (pm.response.code === 400) {
        pm.expect(jsonData).to.have.property('error');
        pm.expect(jsonData.error).to.have.property('message');
        pm.expect(jsonData.error.message).to.include('required field');
    }
});
```

#### Authentication Error

```javascript
pm.test("Authentication error handling", function () {
    if (pm.response.code === 401) {
        var jsonData = pm.response.json();
        pm.expect(jsonData.error).to.eql('Unauthorized');
        pm.expect(jsonData.message).to.include('authentication');
    }
});
```

## Best Practices

### 1. Test Organization

- Group related tests together
- Use descriptive test names
- Include setup and teardown logic

### 2. Data Management

```javascript
// Store test data
const testData = {
    validRequest: {
        name: "Test Resource",
        type: "TEST_TYPE"
    },
    invalidRequest: {
        name: "",
        type: "INVALID"
    }
};

// Use in tests
pm.test("Valid request succeeds", function () {
    // Use testData.validRequest
});
```

## Troubleshooting

### Debug Helpers

```javascript
// Response logging
console.log("Response:", pm.response.json());

// Request logging
console.log("Request:", {
    url: pm.request.url.toString(),
    method: pm.request.method,
    headers: pm.request.headers,
    body: pm.request.body
});

// Variable logging
console.log("Variables:", {
    environment: pm.environment.toObject(),
    globals: pm.globals.toObject(),
    collectionVariables: pm.collectionVariables.toObject()
});
```

## Using in Cursor IDE

### Integration Tips

1. Use consistent formatting for better code generation
2. Include detailed comments for context
3. Structure tests in a modular way
4. Maintain clear separation of concerns

### Example Usage

When creating new endpoints:

1. Copy relevant test template
2. Modify schema validation
3. Add specific business logic tests
4. Include error handling cases

## Maintenance

### Test Updates

1. Review and update test cases when API changes
2. Maintain documentation alignment
3. Update environment variables as needed
4. Keep response schemas current

### Version Control

1. Store collection exports in version control
2. Document collection version changes
3. Track environment configurations
4. Maintain changelog for test updates

## Creating Postman Collections Programmatically

### Collection JSON Format

The following example shows how to structure your Postman collection JSON file with collection-level variables. Save it in the folder `postman-tests` with a descriptive name like `api-endpoints.postman_collection.json`:

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
    },
    {
      "key": "resourceId",
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

### Programmatic Creation

Here's a Node.js script template to generate your Postman collections with collection-level variables:

```javascript
const fs = require('fs');
const path = require('path');

// Helper function to create collection variables
function createCollectionVariables() {
  return [
    {
      key: "baseUrl",
      value: "http://localhost:5000",
      type: "string"
    },
    {
      key: "authToken",
      value: "",
      type: "string"
    },
    {
      key: "tenantId",
      value: "",
      type: "string"
    },
    {
      key: "resourceId",
      value: "",
      type: "string"
    }
  ];
}

// Create your collection
const collection = {
  info: {
    name: "API Endpoints",
    schema: "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  variable: createCollectionVariables(),
  item: [
    {
      name": "Get All Resources",
      request": {
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
      event": [
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
      event": [
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
};

// Save collection to file
fs.writeFileSync(
  path.join(__dirname, 'api-endpoints.postman_collection.json'),
  JSON.stringify(collection, null, 2)
);
```

### Best Practices for Collection Management

1. Version Control
   - Store collections with variables in version control
   - Use meaningful commit messages for collection updates
   - Keep a changelog of major collection changes

2. Organization
   - Group related endpoints into folders
   - Use consistent naming conventions
   - Include relevant examples and documentation

3. Variable Management
   - Define all variables at the collection level
   - Never commit sensitive values in collection files
   - Use descriptive variable names
   - Document required variables

4. Testing
   - Include basic response validation tests
   - Add schema validation where appropriate
   - Chain requests using collection variables
   - Test error scenarios

5. Documentation
   - Add descriptions to requests
   - Include example responses
   - Document prerequisites
   - Keep README updated with setup instructions

Example Project Structure:
```
postman/
├── collections/
│   ├── api-endpoints.postman_collection.json
│   └── integration-tests.postman_collection.json
├── scripts/
│   └── generate-collection.js
└── README.md
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
