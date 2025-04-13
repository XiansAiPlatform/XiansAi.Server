# Creating New Functionality Guide

This guide outlines the step-by-step process for creating new functionality in the XiansAi.Server project. Each step references detailed documentation that should be followed.

## 1. Database Schema

First, create a new JSON schema for your entity in the `Database/Schemas` directory.

See [Database Schema Guidelines](database-schemas.md) for detailed instructions on:

- Schema file naming and location
- Required base properties
- Naming conventions
- Data types
- Schema validation
- Indexing guidelines

## 2. Database Repository

Create the repository layer for your entity:

1. Create a model class in `Database/Models/{EntityName}.cs`
2. Create a repository class in `Database/Repositories/{EntityName}Repository.cs`

See [Repository Pattern Implementation Guide](database-repositories.md) for:

- Model class implementation
- Repository class patterns
- CRUD operations
- Best practices for error handling, validation, and performance
- Testing guidelines

## 3. Service Layer

Implement service classes to encapsulate business logic and orchestrate operations between repositories and endpoints.

See [Service Layer Design](service-layer-design.md) for:

- Design principles (Dependency Injection, SRP)
- Registration patterns
- Best practices

## 4. REST Endpoints

See [End-Point Design](end-point-design.md) for:

- Endpoint class implementation
- Request/response DTOs
- Security requirements
- Error handling
- Documentation standards

See [OpenAPI Documentation](openapi-docs.md) for:

- Endpoint documentation
- Request/response examples
- Error codes
- Security requirements

## 5. Postman Tests

Create a Postman collection for testing your endpoints:

1. Create a new collection file in `Tests/postman/{entity}-api-endpoints.postman_collection.json`
2. Implement test scripts for all CRUD operations
3. Add pre-request and post-request scripts for test data management

See [Postman Test Scripts Guide](client-postman-test.md) for:

- Collection structure
- Variable setup
- Test script implementation
- Auto-generating test data
- Cleanup procedures

## Best Practices

Throughout the implementation process, follow these best practices:

1. **Code Organization**
   - Keep files in appropriate directories
   - Follow naming conventions
   - Maintain consistent code style

2. **Error Handling**
   - Implement proper error handling at each layer
   - Return appropriate HTTP status codes
   - Log errors with sufficient context

3. **Security**
   - Always include tenant authentication
   - Validate input data
   - Follow principle of least privilege

4. **Testing**
   - Write comprehensive tests
   - Include edge cases
   - Test error conditions

5. **Documentation**
   - Add OpenAPI documentation
   - Include clear descriptions
   - Document expected responses

## Example Workflow

Here's a quick example of implementing a new "Task" feature:

1. Create `Database/Schemas/task-schema.json`
2. Create `Database/Models/Task.cs`
3. Create `Database/Repositories/TaskRepository.cs`
4. Create service classes (e.g., `Services/WebApi/TaskService.cs`)
5. Create `Features/WebApi/Endpoints/TaskEndpoint.cs`
6. Create `Tests/postman/task-api-endpoints.postman_collection.json`

Follow the detailed guidelines in each referenced document for the specific implementation details.
