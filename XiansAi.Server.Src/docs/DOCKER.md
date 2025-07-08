# Docker Documentation

This document provides comprehensive instructions for building, publishing, and running the XiansAi Server using Docker.

## Table of Contents

1. [Automated Publishing via GitHub Actions](#automated-publishing-via-github-actions)
2. [Manual Publishing Docker Images to DockerHub (not recommended)](#manual-publishing-docker-images-to-dockerhub-not-recommended)
3. [Running Published Docker Images](#running-published-docker-images)
4. [Building and Testing Docker Images Locally](#building-and-testing-docker-images-locally)
5. [Troubleshooting](#troubleshooting)

---

## Automated Publishing via GitHub Actions

### Overview

The repository includes GitHub Actions automation that automatically builds and publishes Docker images to DockerHub when you create version tags.

### Quick Start

```bash
# Define the version
export VERSION=1.0.0 # or 1.0.0-beta for pre-release

# Create and push a version tag
git tag -a v$VERSION -m "Release v$VERSION"
git push origin v$VERSION
```

### Delete existing tag (optional)

```bash
git tag -d v$VERSION
git push origin :refs/tags/v$VERSION
```

### What Gets Published

The automation publishes to: `99xio/xiansai-server`

**Tags created for stable releases (e.g., `v2.0.0`):**

- `v2.0.0` - Exact version tag
- `2.0.0` - Semantic version
- `2.0` - Major.minor version
- `2` - Major version
- `latest` - Points to the most recent stable release

**Tags created for pre-releases (e.g., `v2.0.0-beta`):**

- `v2.0.0-beta` - Exact version tag
- `2.0.0-beta` - Semantic version
- `2.0` - Major.minor version
- `2` - Major version
- **No `latest` tag** - Pre-releases don't get tagged as latest

### Multi-Platform Support

Images are automatically built for:

- `linux/amd64` (Intel/AMD 64-bit)
- `linux/arm64` (ARM 64-bit, Apple Silicon)

### Monitoring Builds

1. Go to the repository's **Actions** tab on GitHub
2. Look for "Build and Publish to DockerHub" workflows
3. Check DockerHub for newly published images

---

## Manual Publishing Docker Images to DockerHub (not recommended)

### Prerequisites

- Docker installed with buildx support
- DockerHub account and credentials
- Access to the repository

### Step-by-Step Instructions

1. **Set up environment variables:**

   ```bash
   export DOCKERHUB_USERNAME=99xio
   export IMAGE_NAME=99xio/xiansai-server
   export ADDITIONAL_TAGS="latest"
   export TAG="v2.0.0"
   ```

2. **Navigate to the source directory:**

   ```bash
   cd XiansAi.Server.Src
   ```

3. **Run the build and publish script:**

   ```bash
   ./docker-build-and-publish.sh
   ```

### What the Script Does

The `docker-build-and-publish.sh` script performs the following actions:

- **Validates environment variables** - Ensures required variables are set
- **Logs into DockerHub** - Interactive login prompt
- **Creates buildx builder** - Sets up multi-platform building capability
- **Builds multi-platform images** - Creates images for linux/amd64 and linux/arm64
- **Pushes to DockerHub** - Uploads all specified tags

### Environment Variables Explained

| Variable | Description | Example | Required |
|----------|-------------|---------|----------|
| `DOCKERHUB_USERNAME` | Your DockerHub username | `99xio` | Yes |
| `IMAGE_NAME` | Full image name including username | `99xio/xiansai-server` | No (defaults to `xiansai/server`) |
| `TAG` | Primary version tag | `v2.0.0` | No (defaults to `latest`) |
| `ADDITIONAL_TAGS` | Comma-separated additional tags | `latest,stable` | No |
| `DOCKERFILE` | Dockerfile to use | `Dockerfile.production` | No (defaults to `Dockerfile.production`) |
| `PLATFORM` | Target platforms | `linux/amd64,linux/arm64` | No (defaults to both) |

---

## Running Published Docker Images

### Basic Usage

Run the XiansAi Server using the published Docker image:

```bash
docker run -d \
  --name xians-server-standalone \
  --env-file .env \
  -p 5001:8080 \
  --restart unless-stopped \
  99xio/xiansai-server:latest
```

### Command Parameters Explained

| Parameter | Description |
|-----------|-------------|
| `-d` | Run container in detached mode (background) |
| `--name xians-server-standalone` | Assign a name to the container |
| `--env-file .env` | Load environment variables from file |
| `-p 5001:80` | Map host port 5001 to container port 80 |
| `--restart unless-stopped` | Restart policy for container |

### Alternative Port Mapping

For production deployments, you might want to use port 8080:

```bash
docker run -d \
  --name xians-server-standalone \
  --env-file .env \
  -p 5001:8080 \
  --restart unless-stopped \
  99xio/xiansai-server:latest
```

### Health Check

The container includes a health check endpoint:

```bash
# Check container health
docker ps

# Manual health check
curl http://localhost:5001/health
```

### Logs and Monitoring

```bash
# View container logs
docker logs xians-server-standalone

# Follow logs in real-time
docker logs -f xians-server-standalone

# Container stats
docker stats xians-server-standalone
```

---

## Building and Testing Docker Images Locally

### For Development and Testing

1. **Build local image using development Dockerfile:**

   ```bash
   cd XiansAi.Server.Src
   docker build -t xiansai-server:local .
   ```

2. **Run the local image:**

   ```bash
   docker run -d \
     --name xians-server-local \
     --env-file .env \
     -p 5001:8080 \
     xiansai-server:local
   ```

### For Production-like Testing

1. **Build using production Dockerfile:**

   ```bash
   cd XiansAi.Server.Src
   docker build -f Dockerfile.production -t xiansai-server:local-prod .
   ```

2. **Run with production configuration:**

   ```bash
   docker run -d \
     --name xians-server-local-prod \
     --env-file .env \
     -p 5001:8080 \
     -e ASPNETCORE_ENVIRONMENT=Production \
     xiansai-server:local-prod
   ```

### Multi-Platform Local Build

To test multi-platform builds locally:

```bash
# Create and use a new buildx builder
docker buildx create --name local-builder --use

# Build for multiple platforms (without pushing)
docker buildx build \
  --platform linux/amd64,linux/arm64 \
  --file Dockerfile.production \
  --tag xiansai-server:local-multiarch \
  --load \
  .
```

---

## Troubleshooting

### Common Issues

1. **Permission Denied Error:**

   ```bash
   chmod +x docker-build-and-publish.sh
   ```

2. **Docker Login Issues:**

   ```bash
   docker logout
   docker login
   ```

3. **Multi-platform Build Failures:**

   ```bash
   docker buildx rm xiansai-builder
   docker buildx create --name xiansai-builder --use
   ```

4. **Port Already in Use:**

   ```bash
   # Find process using port
   lsof -i :5001
   
   # Use different port
   docker run -p 5002:8080 ...
   ```

5. **Container Won't Start:**

   ```bash
   # Check logs
   docker logs xians-server-standalone
   
   # Check environment variables
   docker exec xians-server-standalone env
   ```

### Health Check Failures

If the container health check fails:

1. Check if the application is listening on the correct port
2. Verify environment variables are set correctly
3. Check application logs for startup errors
4. Ensure all required services (MongoDB, etc.) are accessible

### Memory and Resource Issues

For resource-constrained environments:

```bash
# Limit container resources
docker run -d \
  --name xians-server-standalone \
  --memory=1g \
  --cpus=0.5 \
  --env-file .env \
  -p 5001:8080 \
  99xio/xiansai-server:latest
```

---

## Additional Resources

- [Docker Official Documentation](https://docs.docker.com/)
- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [Docker BuildX Documentation](https://docs.docker.com/buildx/)
- [Multi-platform Builds](https://docs.docker.com/build/building/multi-platform/)
