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

```bash
# Run all services (default)
dotnet run

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
  --name xiansai-server \
  99xio/xiansai-server:latest

# Development with Docker Compose
docker-compose up -d
```

For comprehensive Docker instructions, see [Docker Documentation](./docs/DOCKER.md).

## Secrets Management

The secrets are stored in Azure Key Vault. The Key Vault is configured in the `appsettings.json` file.

Local secrets are stored in:
```
~/.microsoft/usersecrets/<user_secrets_id>/secrets.json
```

For more information, see the [Microsoft documentation](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-9.0&tabs=linux).

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

## Deployment

### Azure Production Deployment

To deploy to Azure Production:

```bash
# Ensure you have correct permissions and clone XiansAi.Infrastructure repository
../XiansAi.Server.Infra/deploy-webapi.sh
```

### Docker Deployment

For Docker build and deployment:

```bash
# Set your DockerHub credentials
export DOCKERHUB_USERNAME=yourusername

# Build and publish
./docker-build-and-publish.sh
```

See [Docker Documentation](./docs/DOCKER.md) for detailed build and deployment instructions.

## Development

### Local Development

```bash
# Start all services
./start.sh

# Start specific service
./start.sh --web    # WebApi only
./start.sh --lib    # LibApi only
./start.sh --user   # UserApi only

# With custom environment
./run-environment.sh
```

### Using Docker Compose

```bash
# Start all services including dependencies
docker-compose up -d

# Start specific service
docker-compose up -d webapi
docker-compose up -d libapi
```

For detailed development setup and configuration options, see [Start Options](./docs/START_OPTIONS.md).
