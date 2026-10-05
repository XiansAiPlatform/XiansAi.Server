import { type UserManagerSettings } from 'oidc-client-ts';
import { type OidcConfig } from '../config';

/**
 * Provider-agnostic OIDC config: no assumptions about Auth0, Keycloak,
 * Entra ID, etc. Whichever provider you use must also be registered against
 * the server's admin-console OIDC config (docs/authentication.md).
 */
export function buildOidcSettings(oidc: OidcConfig): UserManagerSettings {
  return {
    authority: oidc.authority,
    client_id: oidc.clientId,
    scope: oidc.scope,
    redirect_uri: `${window.location.origin}/auth/callback`,
    post_logout_redirect_uri: window.location.origin,
    response_type: 'code',
    // Authorization Code + PKCE, the standard flow for browser-based public clients.
    response_mode: 'query',
  };
}
