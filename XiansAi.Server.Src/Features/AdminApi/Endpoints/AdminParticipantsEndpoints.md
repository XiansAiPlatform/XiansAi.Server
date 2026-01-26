# Participants

## GET

/api/v1/admin/participants/{email}/tenants

When this is called we need to return the participant information in the following format

[
    { 
        "tenantId": "tenant1", 
        "tenantName": "Tenant 1",
        "logo": {
            "url": "https://example.com/logo.png",
            "width": 200,
            "height": 100
        }
    },
    { 
        "tenantId": "tenant2", 
        "tenantName": "Tenant 2",
        "logo": null
    }
]

The tenants should be where the user has TenantParticipant role and tenant.enabled=true
