#!/bin/bash

# Local Docker Build Script for XiansAi Server (Backend)
# This script builds the Docker image locally without publishing to DockerHub

set -e  # Exit on any error

# Default values
IMAGE_NAME="99xio/xiansai-server"
DEFAULT_TAG="local"
DOCKERFILE="./Dockerfile.production"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

print_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

print_error() {
    echo -e "${RED}❌ $1${NC}"
}

# Function to show usage
show_usage() {
    echo "Usage: $0 [OPTIONS]"
    echo ""
    echo "Options:"
    echo "  -t, --tag TAG        Docker image tag (default: ${DEFAULT_TAG})"
    echo "  -n, --name NAME      Docker image name (default: ${IMAGE_NAME})"
    echo "  -f, --file FILE      Dockerfile path (default: ${DOCKERFILE})"
    echo "  -p, --platform ARCH  Target platform (default: local platform)"
    echo "  --no-cache           Build without using cache"
    echo "  -h, --help           Show this help message"
    echo ""
    echo "Examples:"
    echo "  $0                               # Build with default tag 'local'"
    echo "  $0 -t v1.0.0                    # Build with tag 'v1.0.0'"
    echo "  $0 -t dev -n my-xiansai-server   # Build with custom name and tag"
    echo "  $0 -p linux/amd64 -t production # Build for specific platform"
    echo "  $0 --no-cache -t latest          # Build without cache"
}

# Parse command line arguments
TAG="$DEFAULT_TAG"
NO_CACHE=""
PLATFORM=""

while [[ $# -gt 0 ]]; do
    case $1 in
        -t|--tag)
            TAG="$2"
            shift 2
            ;;
        -n|--name)
            IMAGE_NAME="$2"
            shift 2
            ;;
        -f|--file)
            DOCKERFILE="$2"
            shift 2
            ;;
        -p|--platform)
            PLATFORM="$2"
            shift 2
            ;;
        --no-cache)
            NO_CACHE="--no-cache"
            shift
            ;;
        -h|--help)
            show_usage
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            show_usage
            exit 1
            ;;
    esac
done

# Validate inputs
if [[ -z "$TAG" ]]; then
    print_error "Tag cannot be empty"
    exit 1
fi

if [[ -z "$IMAGE_NAME" ]]; then
    print_error "Image name cannot be empty"
    exit 1
fi

if [[ ! -f "$DOCKERFILE" ]]; then
    print_error "Dockerfile not found: $DOCKERFILE"
    exit 1
fi

# Build the full image name with tag
FULL_IMAGE_NAME="${IMAGE_NAME}:${TAG}"

# Print build information
echo ""
print_info "🐳 Building XiansAi Server Docker Image Locally"
echo "=============================================="
print_info "Image Name: ${FULL_IMAGE_NAME}"
print_info "Dockerfile: ${DOCKERFILE}"
if [[ -n "$PLATFORM" ]]; then
    print_info "Platform: ${PLATFORM}"
fi
if [[ -n "$NO_CACHE" ]]; then
    print_warning "Cache disabled"
fi
echo ""

# Construct docker build command
DOCKER_CMD="docker build"
DOCKER_CMD+=" -t ${FULL_IMAGE_NAME}"
DOCKER_CMD+=" -f ${DOCKERFILE}"

if [[ -n "$PLATFORM" ]]; then
    DOCKER_CMD+=" --platform ${PLATFORM}"
fi

if [[ -n "$NO_CACHE" ]]; then
    DOCKER_CMD+=" ${NO_CACHE}"
fi

# Add build context (current directory)
DOCKER_CMD+=" ."

# Show the command that will be executed
print_info "Executing: ${DOCKER_CMD}"
echo ""

# Execute the build
if eval "$DOCKER_CMD"; then
    echo ""
    print_success "🎉 Docker image built successfully!"
    print_success "Image: ${FULL_IMAGE_NAME}"
    echo ""
    
    # Show image info
    print_info "📊 Image Information:"
    docker images "${IMAGE_NAME}" --format "table {{.Repository}}\t{{.Tag}}\t{{.ID}}\t{{.CreatedAt}}\t{{.Size}}" | head -2
    echo ""
    
    # Show usage examples
    print_info "🚀 Quick Start Commands:"
    echo "  # Run the container:"
    echo "  docker run -d --name xiansai-server-${TAG} -p 5000:80 \
    -e ASPNETCORE_ENVIRONMENT=Production \
    ${FULL_IMAGE_NAME}"
    echo ""
    echo "  # Remove the image:"
    echo "  docker rmi ${FULL_IMAGE_NAME}"
    echo ""
else
    print_error "Failed to build Docker image"
    exit 1
fi 