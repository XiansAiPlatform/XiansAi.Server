# DockerHub Automation Guide

This guide explains the automated Docker image building and publishing setup for DockerHub using GitHub Actions.

## 🚀 Overview

The `dockerhub` branch is configured with GitHub Actions to automatically build and publish Docker images to DockerHub whenever commits are pushed to this branch.

## 📋 Setup Requirements

### 1. DockerHub Token Setup

You need to add your DockerHub token as a GitHub secret:

1. Go to your GitHub repository
2. Navigate to **Settings** → **Secrets and variables** → **Actions**
3. Add a new repository secret:
   - **Name:** `DOCKERHUB_TOKEN`
   - **Value:** Your DockerHub access token

### 2. DockerHub Username Configuration

Update the `DOCKERHUB_USERNAME` in `.github/workflows/dockerhub-deploy.yml`:

```yaml
env:
  IMAGE_NAME: xiansai-server
  DOCKERHUB_USERNAME: your-dockerhub-username  # Change this to your username/org
```

## 🏷️ Docker Image Tagging Strategy

The workflow uses an intelligent tagging strategy that combines multiple approaches:

### Automatic Tags Generated

1. **Branch Name**: `dockerhub` (for commits to dockerhub branch)
2. **Git SHA**: `dockerhub-<short-sha>` (unique identifier for each commit)
3. **Latest**: `latest` (always applied to dockerhub branch)
4. **Git Tags**: If you push a git tag like `v1.0.0`, it will create a `v1.0.0` Docker tag

### Example Tag Output

For a commit `abc1234` on the `dockerhub` branch:

- `99xio/xiansai-server:dockerhub`
- `99xio/xiansai-server:dockerhub-abc1234`
- `99xio/xiansai-server:latest`

For a git tag `v1.2.0`:

- `99xio/xiansai-server:v1.2.0`
- `99xio/xiansai-server:latest`

## 🎯 Using Git Tags for Versioning

**Yes, you should use git tags for versioning!** Here's how:

### Creating Version Tags

```bash
# Create a new version tag
git tag -a v2.0.0 -m "Release version 2.0.0"

# Push the tag to trigger Docker build
git push origin v2.0.0

# Or push all tags
git push origin --tags
```

### Semantic Versioning Tags

The workflow supports semantic versioning tags that start with `v`:

- ✅ `v1.0.0` - Major version
- ✅ `v1.2.3` - Patch version  
- ✅ `v2.0.0-beta.1` - Pre-release version
- ✅ `v1.0.0-rc.1` - Release candidate

### Recommended Tagging Workflow

1. **Development**: Push commits to `dockerhub` branch for testing

   ```bash
   git checkout dockerhub
   git commit -m "feat: add new feature"
   git push origin dockerhub
   # → Creates: latest, dockerhub, dockerhub-<sha>
   ```

2. **Release**: Create and push version tags

   ```bash
   git tag -a v1.0.0 -m "Release v1.0.0"
   git push origin v1.0.0
   # → Creates: v1.0.0, latest
   ```

## 🔧 Workflow Triggers

The Docker build is triggered by:

1. **Push to dockerhub branch**

   ```bash
   git push origin dockerhub
   ```

2. **Version tags** (any tag starting with 'v')

   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

3. **Manual trigger** (from GitHub Actions tab)

## 🏗️ Build Details

### Multi-Platform Support

The workflow builds for multiple architectures:

- `linux/amd64` (Intel/AMD 64-bit)
- `linux/arm64` (ARM 64-bit, Apple Silicon)

### Build Context

- **Context**: `./XiansAi.Server.Src`
- **Dockerfile**: `Dockerfile.production`
- **Build Cache**: GitHub Actions cache for faster builds

### Build Output

After each successful build, you'll see a summary with:

- Image name and tags
- Docker pull commands
- Platform information

## 📦 Using the Published Images

### Pull Latest Image

```bash
docker pull 99xio/xiansai-server:latest
```

### Pull Specific Version

```bash
docker pull 99xio/xiansai-server:v1.0.0
```

### Run the Container

```bash
# Using environment file
docker run --env-file .env -p 5000:80 99xio/xiansai-server:latest

# Using docker-compose
docker-compose -f docker-compose.production.yml up -d
```

## 🔍 Monitoring Builds

### GitHub Actions

1. Go to your repository
2. Click **Actions** tab
3. Look for "Build and Publish to DockerHub" workflows

### DockerHub

1. Visit your DockerHub repository
2. Check the **Tags** tab for new images
3. View download statistics and image details

## 🚨 Troubleshooting

### Build Failures

**Authentication Error:**

- Verify `DOCKERHUB_TOKEN` secret is set correctly
- Ensure DockerHub username matches in workflow

**Build Context Error:**

- Check that `XiansAi.Server.Src` directory exists
- Verify `Dockerfile.production` is present

**Platform Build Error:**

- Docker buildx may need initialization
- Check platform compatibility

### Tag Issues

**Missing Tags:**

- Ensure git tags start with 'v' (e.g., `v1.0.0`)
- Check that tags are pushed to GitHub: `git push origin --tags`

**Wrong Tags:**

- Review the metadata-action configuration
- Check the `tags:` section in the workflow

## 🔧 Advanced Configuration

### Custom Image Names

Update the workflow environment variables:

```yaml
env:
  IMAGE_NAME: your-custom-name
  DOCKERHUB_USERNAME: your-org
```

### Additional Platforms

Add more platforms in the build step:

```yaml
platforms: linux/amd64,linux/arm64,linux/arm/v7
```

### Custom Tagging Rules

Modify the metadata extraction step to add custom tagging logic:

```yaml
tags: |
  type=ref,event=branch
  type=ref,event=tag
  type=semver,pattern={{version}}
  type=semver,pattern={{major}}.{{minor}}
```

## 📚 Next Steps

1. Set up the DockerHub token in GitHub secrets
2. Update the DockerHub username in the workflow
3. Test the setup by pushing to the `dockerhub` branch
4. Create your first version tag: `git tag v1.0.0 && git push origin v1.0.0`
5. Monitor the build in GitHub Actions
6. Verify the image appears in DockerHub

## 🔗 Related Documentation

- [Docker Deployment Guide](./DOCKER_DEPLOYMENT.md)
- [Docker Quick Start](./DOCKER_QUICK_START.md)
- [Configuration Management](./CONFIGURATION.md) 