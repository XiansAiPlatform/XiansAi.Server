import { useState } from 'react';
import { createAdminApiClient } from '../adminApi/client';
import { getServerConfig } from '../config';
import { type Identity } from '../auth/useAuth';

export type TenantSwitchStatus = 'idle' | 'validating' | 'success' | 'error';

export interface TenantSwitcherState {
  input: string;
  setInput: (value: string) => void;
  status: TenantSwitchStatus;
  error: string | null;
  submit: () => Promise<void>;
}

/**
 * Validates a tenant id against tenant.agentActivations.list, the one
 * endpoint every tenant role can call, before switching to it. Using a free-text
 * entry, not a picker as there's no endpoint to populate
 * one from (see docs/authentication.md).
 */
export function useTenantSwitcher(identity: Identity, initialTenantId: string | null, onValidated: (tenantId: string) => void): TenantSwitcherState {
  const [input, setInput] = useState(initialTenantId ?? '');
  const [status, setStatus] = useState<TenantSwitchStatus>('idle');
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    const tenantId = input.trim();
    if (!tenantId) return;

    setStatus('validating');
    setError(null);

    try {
      const probe = createAdminApiClient({ ...getServerConfig(), tenantId }, identity);
      await probe.listAgentActivations();
      setStatus('success');
      onValidated(tenantId);
    } catch (err) {
      setStatus('error');
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  return { input, setInput, status, error, submit };
}
