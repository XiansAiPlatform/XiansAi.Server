#!/bin/bash
set -e

# Configuration
DEFAULT_IMAGE_NAME="xiansai/server"
IMAGE_NAME="${IMAGE_NAME:-$DEFAULT_IMAGE_NAME}"
TAG="${TAG:-latest}"
DOCKERFILE="${DOCKERFILE:-Dockerfile.production}"
PLATFORM="${PLATFORM:-linux/amd64,linux/arm64}"

echo "🏗️  Building Docker image..."
echo "Image: $IMAGE_NAME:$TAG"
echo "Dockerfile: $DOCKERFILE"
echo "Platform: $PLATFORM"

# Check if buildx is available
if ! docker buildx version > /dev/null 2>&1; then
    echo "❌ Docker buildx is required for multi-platform builds"
    echo "Please install Docker Desktop or enable buildx"
    exit 1
fi

# Create buildx builder if it doesn't exist
if ! docker buildx inspect xiansai-builder > /dev/null 2>&1; then
    echo "🔧 Creating buildx builder..."
    docker buildx create --name xiansai-builder --use
fi

# Build multi-platform image
echo "🚀 Building multi-platform image..."
docker buildx build \
    --platform "$PLATFORM" \
    --file "$DOCKERFILE" \
    --tag "$IMAGE_NAME:$TAG" \
    --push \
    .

echo "✅ Docker image built and pushed successfully!"
echo "📦 Image: $IMAGE_NAME:$TAG"
echo ""
echo "🎯 Next steps:"
echo "   1. Update your .env file with your configuration"
echo "   2. Run: docker-compose -f docker-compose.production.yml up -d" 