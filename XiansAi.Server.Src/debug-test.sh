#!/bin/bash

# Debug Test Script for AgentService
# This script helps test the API endpoints for debugging

echo "=== AgentService Debug Test Script ==="
echo ""

# Configuration
BASE_URL="http://localhost:5001"
TOKEN="your-jwt-token-here"
TENANT_ID="your-tenant-id-here"
AGENT_NAME="test-agent"

echo "Testing AgentService endpoints..."
echo "Base URL: $BASE_URL"
echo "Agent Name: $AGENT_NAME"
echo ""

# Function to make API calls
make_request() {
    local method=$1
    local endpoint=$2
    local description=$3
    
    echo "=== Testing: $description ==="
    echo "Endpoint: $method $BASE_URL$endpoint"
    echo ""
    
    response=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
        -X "$method" \
        "$BASE_URL$endpoint" \
        -H "Authorization: Bearer $TOKEN" \
        -H "X-Tenant-Id: $TENANT_ID" \
        -H "Content-Type: application/json")
    
    # Extract status code and body
    http_status=$(echo "$response" | grep "HTTP_STATUS:" | cut -d: -f2)
    body=$(echo "$response" | sed '/HTTP_STATUS:/d')
    
    echo "Status Code: $http_status"
    echo "Response:"
    echo "$body" | jq '.' 2>/dev/null || echo "$body"
    echo ""
    echo "---"
    echo ""
}

# Test 1: Get Agent Names
make_request "GET" "/api/client/agents/names" "Get Agent Names"

# Test 2: Get Grouped Definitions
make_request "GET" "/api/client/agents/all" "Get Grouped Definitions"

# Test 3: Get Definitions (Basic)
make_request "GET" "/api/client/agents/$AGENT_NAME/definitions/basic" "Get Definitions (Basic)"

# Test 4: Get Workflow Instances
make_request "GET" "/api/client/agents/$AGENT_NAME/test-workflow/runs" "Get Workflow Instances"

# Test 5: Delete Agent (commented out for safety)
# make_request "DELETE" "/api/client/agents/$AGENT_NAME" "Delete Agent"

echo "=== Debug Test Complete ==="
echo ""
echo "To debug with breakpoints:"
echo "1. Set breakpoints in AgentService.cs"
echo "2. Start debugging with F5 in Cursor"
echo "3. Run this script to trigger the breakpoints"
echo ""
echo "Common breakpoint locations:"
echo "- Line 58: Input validation"
echo "- Line 311: Error logging"
echo "- Line 354: Skip invalid definitions" 