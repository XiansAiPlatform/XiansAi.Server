# XiansAi Server - Docker Quick Start

Get your XiansAi Server running in Docker with just a few commands!

## ⚡ 5-Minute Setup

### 1. Validate Setup
```bash
./validate-setup.sh
```

### 2. Configure Environment
```bash
# Copy environment template
cp .env.example .env

# Edit with your values (minimum required)
nano .env
```

**Required Configuration:**
```bash
# Database (required)
MongoDB__ConnectionString=mongodb+srv://user:pass@cluster.mongodb.net/...

# AI/LLM (required) 
Llm__ApiKey=sk-your-openai-api-key

# Auth (if using authentication)
Auth0__Domain=your-domain.auth0.com
Auth0__ClientId=your-client-id
Auth0__ClientSecret=your-client-secret
```

### 3. Deploy
```bash
# Using pre-built image (recommended)
docker-compose -f docker-compose.production.yml up -d

# OR build and deploy locally
./docker-build.sh
docker-compose -f docker-compose.production.yml up -d
```

### 4. Verify
```bash
# Check status
docker ps

# Check logs
docker logs xiansai-server

# Test health endpoint
curl http://localhost:5000/health
```

## 🚀 Publishing to DockerHub

```bash
# Set your DockerHub username
export DOCKERHUB_USERNAME=yourusername

# Build and publish
./docker-build.sh
./docker-publish.sh

# Update your .env
echo "DOCKER_IMAGE=yourusername/xiansai-server:latest" >> .env
```

## 🔧 Common Commands

```bash
# View logs
docker-compose -f docker-compose.production.yml logs -f

# Restart service
docker-compose -f docker-compose.production.yml restart

# Stop service
docker-compose -f docker-compose.production.yml down

# Update image
docker-compose -f docker-compose.production.yml pull
docker-compose -f docker-compose.production.yml up -d
```

## 🆘 Troubleshooting

**Container won't start?**
```bash
docker logs xiansai-server
```

**Health check failing?**
```bash
docker exec xiansai-server curl -f http://localhost:80/health
```

**Need more help?** 
See detailed guide: [DOCKER_DEPLOYMENT.md](./DOCKER_DEPLOYMENT.md)

## 📋 What's Included

- ✅ **Clean production configuration** (no secrets in files)
- ✅ **Standard .NET Core environment variables** 
- ✅ **Multi-platform Docker image** (AMD64/ARM64)
- ✅ **Production security** (non-root user, health checks)
- ✅ **Build & publish automation** 
- ✅ **Comprehensive documentation**

Your server is now ready for any Docker environment! 🐳 