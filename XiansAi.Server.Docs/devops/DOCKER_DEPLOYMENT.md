# XiansAi Server - Docker Deployment Guide

This guide explains how to deploy the XiansAi Server using Docker and docker-compose with proper environment variable configuration.

## 🚀 Quick Start

### 1. Prepare Environment
```bash
# Copy the environment template
cp .env.example .env

# Edit the .env file with your actual values
nano .env  # or use your preferred editor
```

### 2. Deploy with Docker Compose
```bash
# Run in production mode
docker-compose -f docker-compose.production.yml up -d

# Check logs
docker-compose -f docker-compose.production.yml logs -f

# Check health status
docker ps
```

## 🔧 Configuration

### Environment Variables

The application uses standard .NET Core environment variable naming (`SectionName__PropertyName`). All configuration is externalized through environment variables.

See `.env.example` for all available configuration options.

## 🏗️ Building and Publishing

### Build Docker Image

```bash
# Make scripts executable
chmod +x docker-build.sh docker-publish.sh

# Build for DockerHub (multi-platform)
export IMAGE_NAME=99xio/xiansai-server
./docker-build.sh
```

### Publish to DockerHub

```bash
# Set your DockerHub username
export DOCKERHUB_USERNAME=99xio
export ADDITIONAL_TAGS="v1.0.0,latest"
./docker-publish.sh
```

## 🔒 Security Considerations

### Secrets Management

- **Never commit `.env` files** to version control
- Use Docker Secrets or Kubernetes Secrets in production
- Consider using Azure Key Vault, AWS Secrets Manager, etc.

### Container Security
- Container runs as non-root user (`xiansai`)
- Resource limits are configured
- Health checks are implemented
- Read-only file system for certificates

### TLS/SSL
- Configure reverse proxy (nginx, Traefik) for TLS termination
- Mount certificates as read-only volumes
- Use environment variables for certificate passwords

## 📊 Monitoring and Logging

### Health Checks
The container includes built-in health checks:
```bash
# Check container health
docker ps

# View health check logs
docker inspect xiansai-server | grep -A 20 Health
```

### Application Logs
```bash
# Follow logs
docker-compose -f docker-compose.production.yml logs -f xiansai-server

# View recent logs
docker logs xiansai-server --tail 100
```

### Resource Monitoring
```bash
# Monitor resource usage
docker stats xiansai-server

# View container info
docker inspect xiansai-server
```

## 🔧 Advanced Configuration

### Custom Dockerfile
To customize the build process, create your own Dockerfile based on `Dockerfile.production`.

### Database Migration
The application automatically creates database indexes on startup. For custom migrations:

```bash
# Run one-time migration container
docker run --rm \
  --env-file .env \
  99xio/xiansai-server:latest \
  dotnet XiansAi.Server.dll --migrate
```

### Scaling
```bash
# Scale to multiple instances
docker-compose -f docker-compose.production.yml up -d --scale xiansai-server=3

# Use with load balancer
# Configure nginx or Traefik for load balancing
```

## 🚧 Troubleshooting

### Common Issues

**Container won't start:**
```bash
# Check logs for configuration errors
docker logs xiansai-server

# Verify environment variables
docker exec xiansai-server env | grep -E "(Auth0|MongoDB|Llm)"
```

**Health check failing:**
```bash
# Test health endpoint directly
docker exec xiansai-server curl -f http://localhost:80/health

# Check if ports are bound correctly
docker port xiansai-server
```

**Database connection issues:**
```bash
# Test MongoDB connection
docker run --rm --env-file .env 99xio/xiansai-server:latest \
  dotnet XiansAi.Server.dll --test-db
```

### Performance Tuning

**Memory Usage:**
```yaml
# Adjust in docker-compose.production.yml
deploy:
  resources:
    limits:
      memory: 2G  # Increase if needed
```

**Cache Configuration:**
```bash
# Use Redis for better performance
Cache__Provider=redis
Cache__Redis__ConnectionString=your-redis-connection
```

## 📝 Examples

### Development Override
```bash
# Override for development
echo "Cache__Provider=memory" >> .env.local
echo "Email__Provider=console" >> .env.local

# Use override file
docker-compose -f docker-compose.production.yml -f docker-compose.override.yml up -d
```

### Production with External Services
```yaml
# docker-compose.production.yml with external Redis
version: '3.8'
services:
  xiansai-server:
    # ... existing config
    depends_on:
      - redis
  
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data
      
volumes:
  redis_data:
```

### Nginx Reverse Proxy
```nginx
# /etc/nginx/sites-available/xiansai
server {
    listen 80;
    server_name yourdomain.com;
    
    location / {
        proxy_pass http://localhost:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

## 🔧 Environment Variables Reference

### Complete Configuration List

| Category | Variable | Description | Required |
|----------|----------|-------------|----------|
| **Core** | `ASPNETCORE_ENVIRONMENT` | Environment name | No |
| **Core** | `SERVICE_TYPE` | Service type to run | No |
| **Database** | `MongoDB__ConnectionString` | MongoDB connection | Yes |
| **Database** | `MongoDB__DatabaseName` | Database name | No |
| **AI/LLM** | `Llm__ApiKey` | OpenAI API key | Yes |
| **AI/LLM** | `Llm__Provider` | LLM provider | No |
| **AI/LLM** | `Llm__Model` | Model name | No |
| **Cache** | `Cache__Provider` | Cache provider | No |
| **Cache** | `Cache__Redis__ConnectionString` | Redis connection | Conditional |
| **Auth** | `Auth0__Domain` | Auth0 domain | Conditional |
| **Auth** | `Auth0__ClientId` | Auth0 client ID | Conditional |
| **Auth** | `Auth0__ClientSecret` | Auth0 client secret | Conditional |
| **Email** | `Email__Provider` | Email provider | No |
| **Email** | `Email__Azure__ConnectionString` | Azure connection | Conditional |

For the complete list, see `.env.example`.

## 🚀 Deployment Strategies

### Single Instance
```bash
# Simple single container deployment
docker-compose -f docker-compose.production.yml up -d
```

### High Availability
```bash
# Multiple instances with load balancer
docker-compose -f docker-compose.production.yml up -d --scale xiansai-server=3
```

### Blue-Green Deployment
```bash
# Deploy new version alongside old
docker-compose -f docker-compose.production.yml -p xiansai-green up -d

# Switch traffic (using load balancer)
# Stop old version
docker-compose -f docker-compose.production.yml -p xiansai-blue down
```

For more advanced scenarios, refer to the main documentation or create an issue on GitHub. 