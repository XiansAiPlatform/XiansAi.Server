import { useMemo } from 'react';
import { createAdminApiClient, type AdminApiClient } from './client';
import { getServerConfig } from '../config';
import { type Identity } from '../auth/useAuth';

/**
 * Shared AdminApi client, recreated whenever the tenant changes so every
 * consumer (useTasks, useAgentsAndActivations) naturally refetches. Null
 * until a tenant is selected.
 */
export function useAdminApiClient(identity: Identity, tenantId: string | null): AdminApiClient | null {
  return useMemo(() => {
    if (!tenantId) return null;
    return createAdminApiClient({ ...getServerConfig(), tenantId }, identity);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [identity.participantId, tenantId]);
}
