# Participants

## GET

/api/v1/admin/participants/{email}

When this is called we need to return the participant information in the following format

{
    "isSystemAdmin": false,
    "tenants": [
        { 
            "tenantId": "tenant1", 
            "tenantName": "Tenant 1",
            "logo": { "url": "https://example.com/logo.png", "width": 200, "height": 100 },
            "role": "TenantParticipantAdmin"
        },
        { 
            "tenantId": "tenant2", 
            "tenantName": "Tenant 2",
            "logo": null,
            "role": "TenantParticipant"
        }
    ]
}

The tenants should be where the user has TenantParticipant or TenantParticipantAdmin role and tenant.enabled=true
