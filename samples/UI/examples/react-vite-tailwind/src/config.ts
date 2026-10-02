/**
 * All Xians-specific configuration in one place, read from environment
 * variables. Nothing here is hardcoded to a particular server, tenant, or
 * identity provider. See docs/connecting-to-server.md and
 * docs/authentication.md for what each value means and where to get it.
 */

export interface ServerConfig {
  serverUrl: string;
  /** Seeds the Tenant tab's input; the active tenant is switched at runtime. */
  tenantId?: string;
}

export interface OidcConfig {
  authority: string;
  clientId: string;
  scope: string;
}

function requireEnv(name: string, value: string | undefined): string {
  if (!value) {
    throw new Error(
      `Missing required environment variable ${name}. Copy .env.example to ` +
        `.env.local and fill it in. See README.md for what each value means.`
    );
  }
  return value;
}

export function getServerConfig(): ServerConfig {
  const env = import.meta.env;
  return {
    serverUrl: requireEnv('VITE_XIANS_SERVER_URL', env.VITE_XIANS_SERVER_URL),
    tenantId: env.VITE_XIANS_TENANT_ID,
  };
}

export function getOidcConfig(): OidcConfig {
  const env = import.meta.env;
  return {
    authority: requireEnv('VITE_OIDC_AUTHORITY', env.VITE_OIDC_AUTHORITY),
    clientId: requireEnv('VITE_OIDC_CLIENT_ID', env.VITE_OIDC_CLIENT_ID),
    scope: env.VITE_OIDC_SCOPE || 'openid profile email',
  };
}
