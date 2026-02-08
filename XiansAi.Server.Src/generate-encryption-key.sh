#!/bin/bash

# Script to generate a secure encryption key for secrets encryption
# This generates a 256-bit (32-byte) AES encryption key encoded in base64

echo "Generating AES-256 encryption key..."
echo ""

KEY=$(openssl rand -base64 32)

echo "Generated Encryption Key:"
echo "=========================="
echo "$KEY"
echo ""
echo "Add this to your appsettings.json or environment variables:"
echo ""
echo "appsettings.json:"
echo '{'
echo '  "Encryption": {'
echo "    \"SecretsKey\": \"$KEY\","
echo '    "KeyId": "v1"'
echo '  }'
echo '}'
echo ""
echo "Or as environment variable:"
echo "export Encryption__SecretsKey=\"$KEY\""
echo "export Encryption__KeyId=\"v1\""
echo ""
echo "IMPORTANT SECURITY NOTES:"
echo "- DO NOT commit this key to source control"
echo "- Use different keys for dev/staging/production"
echo "- Store production keys in a secure vault (Azure Key Vault, AWS Secrets Manager)"
echo "- Rotate keys periodically"
echo ""
