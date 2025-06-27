#!/bin/bash
set -e

# Configuration
DOCKERHUB_USERNAME="${DOCKERHUB_USERNAME}"
IMAGE_NAME="${IMAGE_NAME:-xiansai/server}"
TAG="${TAG:-latest}"
ADDITIONAL_TAGS="${ADDITIONAL_TAGS}"

if [ -z "$DOCKERHUB_USERNAME" ]; then
    echo "❌ DOCKERHUB_USERNAME environment variable is required"
    echo "   Example: export DOCKERHUB_USERNAME=yourusername"
    exit 1
fi

echo "📦 Publishing to DockerHub..."
echo "Username: $DOCKERHUB_USERNAME"
echo "Image: $IMAGE_NAME:$TAG"

# Login to DockerHub (if not already logged in)
echo "🔐 Logging in to DockerHub..."
docker login

# Tag for DockerHub if needed
DOCKERHUB_IMAGE="$DOCKERHUB_USERNAME/$(basename $IMAGE_NAME)"
if [ "$IMAGE_NAME" != "$DOCKERHUB_IMAGE" ]; then
    echo "🏷️  Tagging image for DockerHub..."
    docker tag "$IMAGE_NAME:$TAG" "$DOCKERHUB_IMAGE:$TAG"
fi

# Push main tag
echo "⬆️  Pushing $DOCKERHUB_IMAGE:$TAG..."
docker push "$DOCKERHUB_IMAGE:$TAG"

# Push additional tags if specified
if [ -n "$ADDITIONAL_TAGS" ]; then
    for EXTRA_TAG in $(echo $ADDITIONAL_TAGS | tr "," "\n"); do
        echo "🏷️  Tagging and pushing $DOCKERHUB_IMAGE:$EXTRA_TAG..."
        docker tag "$DOCKERHUB_IMAGE:$TAG" "$DOCKERHUB_IMAGE:$EXTRA_TAG"
        docker push "$DOCKERHUB_IMAGE:$EXTRA_TAG"
    done
fi

echo "✅ Successfully published to DockerHub!"
echo "📦 Image: $DOCKERHUB_IMAGE:$TAG"
echo ""
echo "🎯 Next steps:"
echo "   1. Update DOCKER_IMAGE in your .env: DOCKER_IMAGE=$DOCKERHUB_IMAGE:$TAG"
echo "   2. Run: docker-compose -f docker-compose.production.yml up -d" 