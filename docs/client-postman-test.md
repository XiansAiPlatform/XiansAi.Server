# Client API Postman Test Scripts Guide

This document provides instructions for setting up and creating Postman test scripts for the Client API endpoints.

## Setup

### Environment Variables

Configure the following variables in your Postman environment:

```json
{
  "baseUrl": "http://localhost:5000",
  "authToken": "your_auth_token_here",
  "tenantId": "your_tenant_id_here"
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

This revised documentation provides a comprehensive guide on how to create new test scripts, with templates and best practices that can be easily followed and adapted for new endpoints.
