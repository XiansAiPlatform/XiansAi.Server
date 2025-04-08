# Tenants

The system should have functionality to manage tenants. Tenants are different customers using this portal in a multi-tenant environment.

## Tenant Properties

- Name
- Domain
- Description
- Logo
  - URL
  - Width
  - Height
- Timezone
- Agents[]
  - Name
  - IsActive
  - Flows[]
    - Name
    - IsActive

## Tenant API

It should be able to create, update, delete and get tenants. This functionality should be available only for the signed in users.