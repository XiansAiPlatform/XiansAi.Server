#!/bin/bash
set -e

# Configuration
DOCKERHUB_USERNAME="${DOCKERHUB_USERNAME}"
DEFAULT_IMAGE_NAME="xiansai/server"
IMAGE_NAME="${IMAGE_NAME:-$DEFAULT_IMAGE_NAME}"
TAG="${TAG:-latest}"
ADDITIONAL_TAGS="${ADDITIONAL_TAGS}"
DOCKERFILE="${DOCKERFILE:-Dockerfile.production}"
PLATFORM="${PLATFORM:-linux/amd64,linux/arm64}"

# Validate required parameters
if [ -z "$DOCKERHUB_USERNAME" ]; then
    echo "❌ DOCKERHUB_USERNAME environment variable is required"
    echo "   Example: export DOCKERHUB_USERNAME=yourusername"
    exit 1
fi

# Determine target image name
DOCKERHUB_IMAGE="$DOCKERHUB_USERNAME/$(basename $IMAGE_NAME)"

echo "🏗️  Building and Publishing Docker image..."
echo "Username: $DOCKERHUB_USERNAME"
echo "Image: $DOCKERHUB_IMAGE:$TAG"
echo "Dockerfile: $DOCKERFILE"
echo "Platform: $PLATFORM"

if [ -n "$ADDITIONAL_TAGS" ]; then
    echo "Additional Tags: $ADDITIONAL_TAGS"
fi

# Login to DockerHub
echo "🔐 Logging in to DockerHub..."
docker login

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

# Build tags array
TAGS=("$DOCKERHUB_IMAGE:$TAG")
if [ -n "$ADDITIONAL_TAGS" ]; then
    for EXTRA_TAG in $(echo $ADDITIONAL_TAGS | tr "," "\n"); do
        TAGS+=("$DOCKERHUB_IMAGE:$EXTRA_TAG")
    done
fi

# Build tag arguments for docker buildx
TAG_ARGS=""
for tag in "${TAGS[@]}"; do
    TAG_ARGS="$TAG_ARGS --tag $tag"
done

# Build and push multi-platform image
echo "🚀 Building and pushing multi-platform image..."
echo "Tags: ${TAGS[*]}"
docker buildx build \
    --platform "$PLATFORM" \
    --file "$DOCKERFILE" \
    $TAG_ARGS \
    --push \
    .

echo "✅ Docker image built and published successfully!"
echo "📦 Main Image: $DOCKERHUB_IMAGE:$TAG"

if [ -n "$ADDITIONAL_TAGS" ]; then
    echo "📦 Additional Tags:"
    for EXTRA_TAG in $(echo $ADDITIONAL_TAGS | tr "," "\n"); do
        echo "   - $DOCKERHUB_IMAGE:$EXTRA_TAG"
    done
fi

echo ""
echo "🎯 Next steps:"
echo "   1. Update DOCKER_IMAGE in your .env: DOCKER_IMAGE=$DOCKERHUB_IMAGE:$TAG"
echo "   2. Run: docker-compose -f docker-compose.production.yml up -d" 