#!/bin/bash

# Root Certificate Generator using OpenSSL CA
# Supports backdating using -startdate with openssl ca

set -e

DEFAULT_CA_NAME="ParklyAi Root CA"
DEFAULT_DAYS=3650
OUTPUT_DIR="./.certs"
KEY_SIZE=4096
CONFIG_FILE="./openssl.cnf"

usage() {
    echo "Usage: $0 [options]"
    echo "Options:"
    echo "  -p, --password PASSWORD   Password for the root certificate (required)"
    echo "  -n, --name NAME           Common name for the CA (default: $DEFAULT_CA_NAME)"
    echo "  -d, --days DAYS           Validity period in days (default: $DEFAULT_DAYS)"
    echo "  -o, --output DIR          Output directory (default: $OUTPUT_DIR)"
    echo "  -h, --help                Display this help message"
    exit 1
}

while [[ "$#" -gt 0 ]]; do
    case $1 in
        -p|--password) PASSWORD="$2"; shift ;;
        -n|--name) CA_NAME="$2"; shift ;;
        -d|--days) DAYS="$2"; shift ;;
        -o|--output) OUTPUT_DIR="$2"; shift ;;
        -h|--help) usage ;;
        *) echo "Unknown parameter: $1"; usage ;;
    esac
    shift
done

if [ -z "$PASSWORD" ]; then
    echo "Error: Password is required"
    usage
fi

CA_NAME=${CA_NAME:-$DEFAULT_CA_NAME}
DAYS=${DAYS:-$DEFAULT_DAYS}
mkdir -p "$OUTPUT_DIR"

echo "Generating root certificate with the following parameters:"
echo "- CA Name: $CA_NAME"
echo "- Validity: $DAYS days"
echo "- Output Directory: $OUTPUT_DIR"

# Generate private key
echo "Generating private key..."
openssl genrsa -des3 -passout pass:"$PASSWORD" -out "$OUTPUT_DIR/$CA_NAME.key" $KEY_SIZE

# Create minimal openssl.cnf file
cat > "$CONFIG_FILE" <<EOF
[ ca ]
default_ca = CA_default

[ CA_default ]
dir               = ./
new_certs_dir     = \$dir/newcerts
database          = \$dir/index.txt
serial            = \$dir/serial
certificate       = \$dir/$OUTPUT_DIR/$CA_NAME.crt
private_key       = \$dir/$OUTPUT_DIR/$CA_NAME.key
default_days      = $DAYS
default_md        = sha256
preserve          = no
policy            = policy_anything
x509_extensions   = v3_ca

[ policy_anything ]
countryName             = optional
stateOrProvinceName     = optional
localityName            = optional
organizationName        = optional
organizationalUnitName  = optional
commonName              = supplied
emailAddress            = optional

[ req ]
distinguished_name = req_distinguished_name
prompt = no

[ req_distinguished_name ]
CN = $CA_NAME

[ v3_ca ]
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid:always,issuer
basicConstraints = critical, CA:true
keyUsage = critical, digitalSignature, cRLSign, keyCertSign
EOF

# Prepare required files for openssl ca
mkdir -p ./newcerts
touch ./index.txt
echo 1000 > ./serial

# Generate CSR
echo "Generating Certificate Signing Request..."
openssl req -new -key "$OUTPUT_DIR/$CA_NAME.key" \
    -passin pass:"$PASSWORD" \
    -subj "//CN=$CA_NAME" \
    -out "$OUTPUT_DIR/$CA_NAME.csr"

# Calculate backdated start date (1 day ago)
BACKDATE=$(date -u -d "1 day ago" +"%Y%m%d%H%M%SZ")

# Self-sign the certificate with openssl ca
echo "Generating root certificate with backdated start date..."
openssl ca -batch \
    -config "$CONFIG_FILE" \
    -selfsign \
    -in "$OUTPUT_DIR/$CA_NAME.csr" \
    -startdate "$BACKDATE" \
    -days "$DAYS" \
    -extensions v3_ca \
    -out "$OUTPUT_DIR/$CA_NAME.crt" \
    -passin pass:"$PASSWORD"

if [ -f "$OUTPUT_DIR/$CA_NAME.crt" ]; then
    echo "Success! Root certificate generated:"
    echo "- Private key: $OUTPUT_DIR/$CA_NAME.key"
    echo "- Certificate: $OUTPUT_DIR/$CA_NAME.crt"

    echo "Certificate information:"
    openssl x509 -in "$OUTPUT_DIR/$CA_NAME.crt" -noout -text | grep "Subject:" -A 1
    openssl x509 -in "$OUTPUT_DIR/$CA_NAME.crt" -noout -text | grep "Validity" -A 2

    echo "Creating PFX file..."
    PKCS12_FILE="$OUTPUT_DIR/$CA_NAME.pfx"
    openssl pkcs12 -export -out "$PKCS12_FILE" \
        -inkey "$OUTPUT_DIR/$CA_NAME.key" \
        -in "$OUTPUT_DIR/$CA_NAME.crt" \
        -passin pass:"$PASSWORD" -passout pass:"$PASSWORD"

    echo "Creating base64 encoded PFX file..."
    PFX_BASE64_FILE="$OUTPUT_DIR/$CA_NAME.pfx.base64"
    cat "$PKCS12_FILE" | base64 > "$PFX_BASE64_FILE"

    echo "- PFX file: $PKCS12_FILE"
    echo "- Base64 encoded PFX: $PFX_BASE64_FILE"
    echo "Note: The PFX file is protected with the provided password"
else
    echo "Error: Failed to generate certificate"
    exit 1
fi
