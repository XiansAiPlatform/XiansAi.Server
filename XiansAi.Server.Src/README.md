# Xians AI Server

This is the server for the Xians AI platform. It is a .NET 7 application that uses the OpenAI API to generate AI responses. It also uses the Temporal API to schedule and run AI workflows.

## Documentation

For detailed configuration and deployment information, see the [docs](./docs/) folder:

- **[Authentication Configuration](./docs/AUTH_CONFIGURATION.md)** - Complete guide for configuring Auth0, Azure AD, Azure B2C, and Keycloak
- **[Docker Documentation](./docs/DOCKER.md)** - Docker build, publish, and deployment instructions
- **[Start Options](./docs/START_OPTIONS.md)** - Application startup options and microservice configuration

## Quick Start

### Prerequisites

- .NET 9 SDK
- MongoDB (local or remote)
- Docker (optional, for containerized deployment)

### Running the Application

Ensure you have the correct environment file (.env.* or appsettings.json) in the root of the project.

```bash
# Run all services (default)
dotnet run

# Run with specific environment file
dotnet run --env-file .env.local

# Run with file watching for development
dotnet watch run

# Run in production mode
dotnet run --launch-profile Production
```

For detailed startup options and microservice configuration, see [Start Options](./docs/START_OPTIONS.md).

### Using Docker

```bash
# Quick start with published image
docker run --rm -it \
  --env-file .env \
  -p 5001:8080 \
  --name xiansai-server-dev \
  99xio/xiansai-server:latest

```

For comprehensive Docker instructions, see [Docker Documentation](./docs/DOCKER.md).

## Architecture

### Microservices

The application can be run as separate microservices or as a single combined service:

1. **WebApi** - Client-facing API endpoints (`api/client/*`)
   - Handles client communication
   - Manages workflows, instructions, and definitions

2. **LibApi** - Server-to-server API endpoints (`api/server/*`)
   - Handles internal service communication
   - Processes activities, signals, and agent tasks

3. **UserApi** - User-facing API endpoints
   - Handles user interactions
   - Manages webhooks and websockets

Each microservice can be scaled independently. See [Start Options](./docs/START_OPTIONS.md) for detailed configuration.

## Authentication Providers

The application supports multiple authentication providers:

- **Auth0** - Third-party authentication service
- **Azure AD/Entra ID** - Microsoft's enterprise identity platform
- **Azure B2C** - Microsoft's customer identity platform
- **Keycloak** - Open-source identity and access management

For complete configuration instructions, see [Authentication Configuration](./docs/AUTH_CONFIGURATION.md).

### DockerHub Deployment

For Docker build and deployment:

```bash
# Set your DockerHub credentials
export DOCKERHUB_USERNAME=yourusername

# Build and publish
./docker-build-and-publish.sh
```

See [Docker Documentation](./docs/DOCKER.md) for detailed build and deployment instructions.

## Recent Fixes

### Temporal Client Service Shutdown Hanging (2025-01-27)

**Issue**: Application would hang during shutdown when Temporal clients took too long to dispose, requiring multiple Ctrl+C signals to force shutdown.

**Solution**: Updated `TemporalClientService` to implement proper async disposal pattern:
- Added `IAsyncDisposable` implementation alongside `IDisposable`
- Implemented 10-second timeout for disposal operations
- Added proper error isolation for individual client disposals
- Improved logging for disposal process monitoring

**Files Modified**:
- `Shared/Utils/Temporal/TemporalClientService.cs`
- `docs/TEMPORAL_CONFIGURATION.md`

**Testing**: Monitor application shutdown times and check logs for "Temporal client service disposed successfully" messages.
