import { useCallback, useEffect, useState } from 'react';
import { AdminApiError, type Agent, type AdminApiClient, type AgentActivation } from '../adminApi/client';

export interface AgentsState {
  agents: Agent[];
  agentsError: string | null;
  agentsErrorStatus?: number;
  activations: AgentActivation[];
  activationsError: string | null;
  activationsErrorStatus?: number;
  loading: boolean;
  refresh: () => void;
}

/**
 * Lists agent deployments and their activations together, fetched and
 * caught independently since they require different minimum roles:
 * tenant.agentDeployments.list excludes TenantParticipant,
 * tenant.agentActivations.list is open to everyone. See
 * docs/optional-features.md.
 */
export function useAgentsAndActivations(client: AdminApiClient | null): AgentsState {
  const [agents, setAgents] = useState<Agent[]>([]);
  const [agentsError, setAgentsError] = useState<string | null>(null);
  const [agentsErrorStatus, setAgentsErrorStatus] = useState<number | undefined>(undefined);
  const [activations, setActivations] = useState<AgentActivation[]>([]);
  const [activationsError, setActivationsError] = useState<string | null>(null);
  const [activationsErrorStatus, setActivationsErrorStatus] = useState<number | undefined>(undefined);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(() => {
    if (!client) return;
    setLoading(true);

    const deployments = client
      .listAgentDeployments({ pageSize: 50 })
      .then((result) => {
        setAgents(result.agents);
        setAgentsError(null);
        setAgentsErrorStatus(undefined);
      })
      .catch((err) => {
        setAgentsError(err instanceof Error ? err.message : String(err));
        setAgentsErrorStatus(err instanceof AdminApiError ? err.status : undefined);
      });

    const activationsCall = client
      .listAgentActivations()
      .then((result) => {
        setActivations(result);
        setActivationsError(null);
        setActivationsErrorStatus(undefined);
      })
      .catch((err) => {
        setActivationsError(err instanceof Error ? err.message : String(err));
        setActivationsErrorStatus(err instanceof AdminApiError ? err.status : undefined);
      });

    Promise.allSettled([deployments, activationsCall]).finally(() => setLoading(false));
  }, [client]);

  useEffect(refresh, [refresh]);

  return {
    agents,
    agentsError,
    agentsErrorStatus,
    activations,
    activationsError,
    activationsErrorStatus,
    loading,
    refresh,
  };
}
