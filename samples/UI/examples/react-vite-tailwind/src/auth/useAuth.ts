import { useEffect, useRef, useState } from 'react';
import { UserManager, type User } from 'oidc-client-ts';
import { getOidcConfig } from '../config';
import { buildOidcSettings } from './oidcConfig';

/**
 * getIdToken returns the OIDC ID token, not the access token, since that's
 * what AdminApi's keyless X-User-Token path validates.
 *
 * No role field: AdminApi has no self-service "what's my role" endpoint
 * reachable this way (the one that returns it, GET /participants/{email},
 * is SysAdmin-only and non-delegable). See docs/authentication.md.
 */
export interface Identity {
  participantId: string;
  name?: string;
  email?: string;
  getIdToken: () => Promise<string>;
}

type AuthState =
  | { status: 'loading' }
  | { status: 'ready'; identity: Identity; signOut: () => void }
  | { status: 'error'; message: string };

/**
 * Placeholder values copied verbatim from .env.example. Catching these up
 * front gives a clear in-app message instead of a DNS failure from trying to
 * reach a host that was never meant to be real.
 */
function looksUnconfigured(oidc: ReturnType<typeof getOidcConfig>): string | null {
  if (oidc.authority.includes('your-idp.example.com')) {
    return 'VITE_OIDC_AUTHORITY is still the placeholder from .env.example.';
  }
  if (oidc.clientId === 'your-client-id') {
    return 'VITE_OIDC_CLIENT_ID is still the placeholder from .env.example.';
  }
  return null;
}

/**
 * Drives a standard OIDC Authorization Code + PKCE login, then hands back an
 * Identity for AdminApi's keyless auth (docs/authentication.md, Option B).
 * This is the only place that knows about oidc-client-ts; everything
 * downstream just consumes the resulting Identity.
 */
export function useAuth(): AuthState {
  const oidc = getOidcConfig();
  const [state, setState] = useState<AuthState>({ status: 'loading' });
  const userManagerRef = useRef<UserManager | null>(null);

  useEffect(() => {
    const unconfigured = looksUnconfigured(oidc);
    if (unconfigured) {
      setState({
        status: 'error',
        message: `${unconfigured} Fill in .env.local with your own identity provider's ` +
          `details, see docs/authentication.md.`,
      });
      return;
    }

    const userManager = new UserManager(buildOidcSettings(oidc));
    userManagerRef.current = userManager;

    const toIdentity = (user: User): Identity => ({
      participantId: user.profile.email ?? user.profile.sub,
      name: user.profile.name,
      email: user.profile.email,
      getIdToken: async () => {
        const token = (await userManager.getUser())?.id_token ?? user.id_token;
        if (!token) throw new Error('No valid OIDC ID token available. Sign in again.');
        return token;
      },
    });

    // RP-initiated logout (see oidcConfig.ts for post_logout_redirect_uri).
    // Falls back to clearing the local session if the provider's discovery
    // document has no end_session_endpoint.
    const signOut = () => {
      userManager.signoutRedirect().catch(() => {
        userManager.removeUser().finally(() => window.location.replace('/'));
      });
    };

    (async () => {
      // Completing a redirect back from the identity provider.
      if (window.location.pathname === '/auth/callback') {
        const user = await userManager.signinRedirectCallback();
        window.history.replaceState({}, document.title, '/');
        setState({ status: 'ready', identity: toIdentity(user), signOut });
        return;
      }

      const existingUser = await userManager.getUser();
      if (existingUser && !existingUser.expired) {
        setState({ status: 'ready', identity: toIdentity(existingUser), signOut });
        return;
      }

      // No valid session yet, so send the user to the identity provider.
      await userManager.signinRedirect();
    })().catch((err) => {
      setState({
        status: 'error',
        message:
          `Couldn't reach the identity provider at ${oidc.authority}: ` +
          `${err instanceof Error ? err.message : String(err)}. Check VITE_OIDC_AUTHORITY ` +
          `and VITE_OIDC_CLIENT_ID in .env.local, see docs/authentication.md.`,
      });
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return state;
}
