#!/bin/bash

# Test Script for Adding Definitions
# This script helps test the definition creation API

echo "=== Definition Creation Test Script ==="
echo ""

# Configuration
BASE_URL="http://localhost:5001"
CERTIFICATE="your-certificate-here"
AGENT_NAME="TestAgent"
WORKFLOW_TYPE="test-workflow-$(date +%s)"

echo "Testing definition creation..."
echo "Base URL: $BASE_URL"
echo "Agent Name: $AGENT_NAME"
echo "Workflow Type: $WORKFLOW_TYPE"
echo ""

# Function to create a test definition
create_test_definition() {
    local description=$1
    
    echo "=== Creating Test Definition: $description ==="
    
    # Create the JSON payload
    cat > /tmp/definition_request.json << EOF
{
  "agent": "$AGENT_NAME",
  "workflowType": "$WORKFLOW_TYPE",
  "source": "Test workflow definition for $description",
  "activityDefinitions": [
    {
      "activityName": "TestActivity",
      "agentToolNames": ["test_tool_1", "test_tool_2"],
      "knowledgeIds": ["test_knowledge_1", "test_knowledge_2"],
      "parameterDefinitions": [
        {
          "name": "testParam1",
          "type": "string"
        },
        {
          "name": "testParam2",
          "type": "integer"
        }
      ]
    },
    {
      "activityName": "AnotherActivity",
      "agentToolNames": ["another_tool"],
      "knowledgeIds": ["another_knowledge"],
      "parameterDefinitions": [
        {
          "name": "anotherParam",
          "type": "boolean"
        }
      ]
    }
  ],
  "parameterDefinitions": [
    {
      "name": "workflowParam1",
      "type": "string"
    },
    {
      "name": "workflowParam2",
      "type": "number"
    }
  ]
}
EOF

    # Make the API call
    response=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
        -X POST \
        "$BASE_URL/api/agent/definitions" \
        -H "Content-Type: application/json" \
        -H "X-Certificate: $CERTIFICATE" \
        -d @/tmp/definition_request.json)
    
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

# Function to test invalid definition
test_invalid_definition() {
    echo "=== Testing Invalid Definition ==="
    
    # Create invalid JSON payload (missing required fields)
    cat > /tmp/invalid_definition.json << EOF
{
  "agent": "$AGENT_NAME",
  "source": "Invalid definition missing required fields"
}
EOF

    response=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
        -X POST \
        "$BASE_URL/api/agent/definitions" \
        -H "Content-Type: application/json" \
        -H "X-Certificate: $CERTIFICATE" \
        -d @/tmp/invalid_definition.json)
    
    http_status=$(echo "$response" | grep "HTTP_STATUS:" | cut -d: -f2)
    body=$(echo "$response" | sed '/HTTP_STATUS:/d')
    
    echo "Status Code: $http_status"
    echo "Response:"
    echo "$body" | jq '.' 2>/dev/null || echo "$body"
    echo ""
    echo "---"
    echo ""
}

# Function to verify definition was created
verify_definition() {
    echo "=== Verifying Definition Creation ==="
    echo "Note: This requires JWT authentication for WebApi endpoints"
    echo "You would need to call:"
    echo "curl -X GET \"$BASE_URL/api/client/agents/$AGENT_NAME/definitions/basic\" \\"
    echo "  -H \"Authorization: Bearer your-jwt-token\" \\"
    echo "  -H \"X-Tenant-Id: your-tenant-id\""
    echo ""
}

# Main execution
echo "1. Creating valid definition..."
create_test_definition "Valid Definition"

echo "2. Testing invalid definition..."
test_invalid_definition

echo "3. Verifying definition creation..."
verify_definition

echo "=== Test Complete ==="
echo ""
echo "To debug definition creation:"
echo "1. Set breakpoints in DefinitionsService.CreateAsync()"
echo "2. Start debugging with F5 in Cursor"
echo "3. Run this script to trigger the breakpoints"
echo ""
echo "Common breakpoint locations:"
echo "- Line 93: CreateAsync method start"
echo "- Line 100: Permission check"
echo "- Line 120: Definition creation"
echo "- Line 157: CreateFlowDefinitionFromRequest"
echo ""
echo "Cleanup:"
echo "rm -f /tmp/definition_request.json /tmp/invalid_definition.json" 